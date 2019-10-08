using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Microsoft.CSharp.RuntimeBinder;
using Binder = Microsoft.CSharp.RuntimeBinder.Binder;
using Tokens = System.Collections.Generic.Queue<string>;
using CharHashSet = System.Collections.Generic.HashSet<char>;
using StringHashSet = System.Collections.Generic.HashSet<string>;
using Characters = System.Collections.Generic.Queue<char>;
using Stack = System.Collections.Generic.Stack<object>;
using Items = System.Collections.Generic.List<object>;
using Words = Atom2.ScopedDictionary<string, object>;

#pragma warning disable 618

namespace Atom2
{
  public sealed partial class Runtime
  {
    private const char Eof = char.MinValue;
    private const char Whitespace = char.MaxValue;
    private const char Colon = ':';
    private const char LeftParenthesis = '(';
    private const char RightParenthesis = ')';
    private const char Quote = '"';
    private const char LeftAngle = '<';
    private const char RightAngle = '>';
    private const char Pipe = '|';
    private const string LoadFilePragma = "load-file";
    private readonly string PragmaToken = "pragma";
    private readonly StringHashSet BlockBeginTokens = new StringHashSet { LeftParenthesis.ToString(), LeftAngle.ToString(), Pipe.ToString() };
    private readonly StringHashSet BlockEndTokens = new StringHashSet { RightParenthesis.ToString(), RightAngle.ToString() };
    private readonly Words setWords = new Words();
    private readonly Words putWords = new Words();
    private readonly Stack stack = new Stack();
    private readonly CharHashSet stringStopCharacters = new CharHashSet { Eof, Quote, LeftParenthesis, RightParenthesis, LeftAngle, RightAngle, Pipe };
    private readonly CharHashSet tokenStopCharacters = new CharHashSet { Eof, Quote, LeftParenthesis, RightParenthesis, LeftAngle, RightAngle, Pipe, Whitespace };
    private static string BaseDirectory { get; set; }

    private Runtime(string baseDirectory)
    {
      BaseDirectory = baseDirectory;
      setWords.Add("trace", new Action(Trace));
      setWords.Add("break", new Action(Break));
      setWords.Add("invoke", new Action(Invoke));
      setWords.Add(")", new Action(DoNothing));
      setWords.Add(">", new Action(Execute));
      setWords.Add("execute", new Action(Execute));
      setWords.Add("|", new Action(Put));
      setWords.Add("ones-complement", UnaryAction(ExpressionType.OnesComplement));
      setWords.Add("equal", BinaryAction(ExpressionType.Equal));
      setWords.Add("not-equal", BinaryAction(ExpressionType.NotEqual));
      setWords.Add("less-or-equal", BinaryAction(ExpressionType.LessThanOrEqual));
      setWords.Add("less", BinaryAction(ExpressionType.LessThan));
      setWords.Add("greater-or-equal", BinaryAction(ExpressionType.GreaterThanOrEqual));
      setWords.Add("greater", BinaryAction(ExpressionType.GreaterThan));
      setWords.Add("add", BinaryAction(ExpressionType.Add));
      setWords.Add("subtract", BinaryAction(ExpressionType.Subtract));
      setWords.Add("multiply", BinaryAction(ExpressionType.Multiply));
      setWords.Add("divide", BinaryAction(ExpressionType.Divide));
      setWords.Add("put", new Action(Put));
      setWords.Add("set", new Action(Set));
      setWords.Add("get", new Action(Get));
      setWords.Add("if", new Action(If));
      setWords.Add("while", new Action(While));
      setWords.Add("evaluate", new Action(Evaluate));
      setWords.Add("length", new Action(Length));
      setWords.Add("split", new Action(Split));
      setWords.Add("evaluate-and-split", new Action(EvaluateAndSplit));
      setWords.Add("join", new Action(Join));
      setWords.Add("cast", new Action(Cast));
      setWords.Add("create-event-handler", new Action(CreateEventHandler));
      setWords.Add("Runtime", typeof(Runtime));
      setWords.Add("runtime", this);
      setWords.Add("show", new Action(Show));
    }

    private void DoNothing()
    {
    }

    private void Show()
    {
      MessageBox.Show(Pop().ToString());
    }

    public static void Main(params string[] arguments)
    {
      try
      {
        new Runtime(arguments[0]).Run(arguments[1]);
        Console.Write("done");
      }
      catch (Exception exception)
      {
        Console.Write(exception.Message);
      }
    }

    private void Break()
    {
      Debugger.Break();
    }

    private static string Code(string filename)
    {
      return File.ReadAllText(BaseDirectory + "/" + filename);
    }

    private static string GetToken(Characters characters, HashSet<char> stopCharacters)
    {
      string result = "";
      while (!stopCharacters.Contains(NextCharacter(characters)))
      {
        result += characters.Dequeue();
      }
      return result;
    }

    private static char NextCharacter(Characters characters)
    {
      char result = (0 < characters.Count) ? characters.Peek() : char.MinValue;
      return result == Eof ? result : char.IsWhiteSpace(result) ? Whitespace : result;
    }

    private static object ToObject(string token)
    {
      if (int.TryParse(token, out int intValue))
      {
        return intValue;
      }
      if (double.TryParse(token, out double doubleValue))
      {
        return doubleValue;
      }
      return token;
    }

    private Action BinaryAction(ExpressionType expressionType)
    {
      Type objectType = typeof(object);
      ParameterExpression parameterA = Expression.Parameter(objectType);
      ParameterExpression parameterB = Expression.Parameter(objectType);
      CSharpArgumentInfo argumentInfo = CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null);
      CSharpArgumentInfo[] argumentInfos = { argumentInfo, argumentInfo };
      CallSiteBinder binder = Binder.BinaryOperation(CSharpBinderFlags.None, expressionType, objectType, argumentInfos);
      DynamicExpression expression = Expression.Dynamic(binder, objectType, parameterB, parameterA);
      LambdaExpression lambda = Expression.Lambda(expression, parameterA, parameterB);
      Delegate function = lambda.Compile();
      return delegate { Push(function.DynamicInvoke(Pop(), Pop())); };
    }

    private void Call()
    {
      Execute();
    }

    private void Cast()
    {
      Type type = (Type) Pop();
      object instance = Pop();
      Push(Expression.Lambda(Expression.Convert(Expression.Constant(instance), type)).Compile().DynamicInvoke());
    }

    private void CreateEventHandler()
    {
      Items items = (Items) Pop();
      EventHandler action = (sender, eventArguments) => EventHandler(items, sender, eventArguments);
      Push(action);
    }

    private void Evaluate(object item)
    {
      if (item is Items list)
      {
        list.ForEach(Process);
        return;
      }
      Process(item);
    }

    private void Evaluate()
    {
      Evaluate(Pop());
    }

    private void EvaluateAndSplit()
    {
      object items = Pop();
      int stackLength = stack.Count;
      Evaluate(items);
      Push(stack.Count - stackLength);
    }

    private void EventHandler(Items items, object sender, EventArgs eventArguments)
    {
      Push(sender);
      Push(eventArguments);
      Evaluate(items);
    }

    private void Execute()
    {
      EvaluateAndSplit();
      int argumentsCount = (int) Pop();
      string memberName = (string) Pop();
      object[] arguments = Pop(argumentsCount - 1).ToArray();
      object typeOrTarget = Pop();
      bool isType = typeOrTarget is Type;
      Type type = isType ? (Type) typeOrTarget : typeOrTarget.GetType();
      object target = isType ? null : typeOrTarget;
      BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic;
      bool hasReturnValue = false;
      switch (memberName)
      {
        case "new":
          memberName = "";
          hasReturnValue = true;
          bindingFlags |= BindingFlags.Instance | BindingFlags.CreateInstance;
          break;
        default:
          bindingFlags |= BindingFlags.Static | BindingFlags.Instance;
          MemberInfo member = type.GetMember(memberName, bindingFlags | BindingFlags.Static).FirstOrDefault();
          if (member != null)
          {
            switch (member)
            {
              case MethodInfo methodInfo:
                hasReturnValue = methodInfo.ReturnType != typeof(void);
                bindingFlags |= BindingFlags.InvokeMethod;
                break;
              case FieldInfo _:
                hasReturnValue = arguments.Length == 0;
                bindingFlags |= (hasReturnValue ? BindingFlags.GetField : BindingFlags.SetField);
                break;
              case PropertyInfo _:
                hasReturnValue = arguments.Length == 0;
                bindingFlags |= (hasReturnValue ? BindingFlags.GetProperty : BindingFlags.SetProperty);
                break;
              case EventInfo eventInfo:
                memberName = eventInfo.AddMethod.Name;
                bindingFlags |= BindingFlags.InvokeMethod;
                break;
            }
          }
          break;
      }
      object invokeResult = type.InvokeMember(memberName, bindingFlags, null, target, arguments);
      if (hasReturnValue)
      {
        Push(invokeResult);
      }
    }

    private void Get()
    {
      foreach (string currentKey in ((Items) Pop()).Select(currentItem => currentItem.ToString()))
      {
        Push(putWords.ContainsKey(currentKey) ? putWords[currentKey] : setWords[currentKey]);
      }
    }

    private Items GetItems(Tokens tokens, out string lastToken)
    {
      lastToken = "";
      Items result = new Items();
      while (0 < tokens.Count)
      {
        string currentToken = tokens.Dequeue();
        lastToken = currentToken;
        if (BlockBeginTokens.Contains(currentToken))
        {
          OnBlockBegin(currentToken);
          result.Add(GetItems(tokens, out string currentLastToken));
          result.Add(currentLastToken);
        }
        else if (BlockEndTokens.Contains(currentToken))
        {
          OnBlockEnd(currentToken);
          break;
        }
        else
        {
          result.Add(ToObject(currentToken));
        }
      }
      return result;
    }

    private void OnBlockBegin(string token)
    {
      if (token == Pipe.ToString())
      {
        BlockBeginTokens.Remove(Pipe.ToString());
        BlockEndTokens.Add(Pipe.ToString());
      }
    }

    private void OnBlockEnd(string token)
    {
      if (token == Pipe.ToString())
      {
        BlockEndTokens.Remove(Pipe.ToString());
        BlockBeginTokens.Add(Pipe.ToString());
      }
    }

    private Tokens GetTokens(string code)
    {
      Tokens result = new Tokens();
      Characters characters = new Characters(code.ToCharArray());
      for (char nextCharacter = NextCharacter(characters); nextCharacter != Eof; nextCharacter = NextCharacter(characters))
      {
        switch (nextCharacter)
        {
          case Whitespace:
            characters.Dequeue();
            break;
          case Quote:
            characters.Dequeue();
            result.Enqueue(GetToken(characters, stringStopCharacters));
            characters.Dequeue();
            break;
          case LeftParenthesis:
          case RightParenthesis:
          case LeftAngle:
          case RightAngle:
          case Pipe:
          case Colon:
            characters.Dequeue();
            result.Enqueue(nextCharacter.ToString());
            break;
          default:
            string currentToken = GetToken(characters, tokenStopCharacters);
            if (currentToken == PragmaToken)
            {
              HandlePragma(result);
            }
            else
            {
              result.Enqueue(currentToken);
            }
            break;
        }
      }
      return result;
    }

    private void HandlePragma(Tokens tokens)
    {
      string pragma = tokens.Dequeue();
      switch (pragma)
      {
        case LoadFilePragma:
          Run(tokens.Dequeue());
          break;
      }
    }

    private void If()
    {
      object condition = Pop();
      object body = Pop();
      Evaluate(condition);
      if ((dynamic) Pop())
      {
        Evaluate(body);
      }
    }

    private void Invoke()
    {
      BindingFlags memberKind = (BindingFlags) Pop();
      BindingFlags memberType = (BindingFlags) Pop();
      string memberName = (string) Pop();
      string typeName = (string) Pop();
      string assemblyName = (string) Pop();
      int argumentsCount = (int) Pop();
      object[] arguments = Pop(argumentsCount).ToArray();
      Assembly assembly = Assembly.LoadWithPartialName(assemblyName);
      Type type = assembly.GetType(typeName);
      BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | memberKind | memberType;
      bool isInstance = memberType.HasFlag(BindingFlags.Instance);
      bool isConstructor = memberKind.HasFlag(BindingFlags.CreateInstance);
      object target = isInstance && !isConstructor ? Pop() : null;
      object result = type.InvokeMember(memberName, bindingFlags, null, target, arguments);
      Push(result);
    }

    private void Join()
    {
      Push(new Items(Pop((int) Pop())));
    }

    private void Length()
    {
      Push(((Items) Pop()).Count);
    }

    private IEnumerable<object> Pop(int count)
    {
      object[] result = new object[count];
      for (int i = count - 1; 0 <= i; --i)
      {
        result[i] = stack.Pop();
      }
      return result;
    }

    private object Pop()
    {
      return stack.Pop();
    }

    private void Push(object item)
    {
      stack.Push(item);
    }

    private void Process(object item)
    {
      switch (TryGetWord(item.ToString(), out object word))
      {
        case WordKind.Set:
          if (word is Action action)
          {
            action.Invoke();
            return;
          }
          putWords.EnterScope();
          Evaluate(word);
          putWords.LeaveScope();
          return;
        case WordKind.Put:
          Push(word);
          return;
        default:
          Push(item);
          return;
      }
    }

    private void Put()
    {
      foreach (object currentItem in Enumerable.Reverse((Items) Pop()))
      {
        putWords[currentItem.ToString()] = Pop();
      }
    }

    private void Run(string filename)
    {
      Evaluate(GetItems(GetTokens(Code(filename)), out _));
    }

    private void Set()
    {
      foreach (object currentItem in Enumerable.Reverse((Items) Pop()))
      {
        setWords[currentItem.ToString()] = Pop();
      }
    }

    private void Split()
    {
      Items items = (Items) Pop();
      foreach (object currentItem in items)
      {
        Push(currentItem);
      }
      Push(items.Count);
    }

    private void Trace()
    {
      object item = stack.Peek();
      MessageBox.Show(item == null ? "(empty)" : $"{item} ({item.GetType().Name})");
    }

    private WordKind TryGetWord(string key, out object word)
    {
      if (putWords.TryGetValue(key, out word))
      {
        return WordKind.Put;
      }
      if (setWords.TryGetValue(key, out word))
      {
        return WordKind.Set;
      }
      return WordKind.None;
    }

    private Action UnaryAction(ExpressionType expressionType)
    {
      Type objectType = typeof(object);
      ParameterExpression parameter = Expression.Parameter(objectType);
      CSharpArgumentInfo argumentInfo = CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null);
      CSharpArgumentInfo[] argumentInfos = { argumentInfo };
      CallSiteBinder binder = Binder.UnaryOperation(CSharpBinderFlags.None, expressionType, objectType, argumentInfos);
      DynamicExpression expression = Expression.Dynamic(binder, objectType, parameter);
      LambdaExpression lambda = Expression.Lambda(expression, parameter);
      Delegate function = lambda.Compile();
      return delegate { Push(function.DynamicInvoke(Pop())); };
    }

    private void While()
    {
      object condition = Pop();
      object body = Pop();
      Evaluate(condition);
      while ((dynamic) Pop())
      {
        Evaluate(body);
        Evaluate(condition);
      }
    }
  }
}