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
using Tokens = System.Collections.Generic.Queue<object>;
using CharHashSet = System.Collections.Generic.HashSet<char>;
using NameHashSet = System.Collections.Generic.HashSet<Atom2.Runtime.Name>;
using Characters = System.Collections.Generic.Queue<char>;
using Stack = System.Collections.Generic.Stack<object>;
using Items = System.Collections.Generic.List<object>;
using Words = Atom2.ScopedDictionary<Atom2.Runtime.Name, object>;

#pragma warning disable 618

namespace Atom2
{
  public sealed partial class Runtime
  {
    public sealed class Name
    {
      public string Value { get; set; }

      public override bool Equals(object value)
      {
        return value is Name name && Value == name.Value;
      }

      public override int GetHashCode()
      {
        return (Value != null ? Value.GetHashCode() : 0);
      }
    }

    private const char Eof = char.MinValue;
    private const char LeftAngle = '<';
    private const char LeftParenthesis = '(';
    private const string LoadFilePragma = "load-file";
    private const char Pipe = '|';
    private const string PragmaToken = "pragma";
    private const char Quote = '"';
    private const char RightAngle = '>';
    private const char RightParenthesis = ')';
    private const char Whitespace = char.MaxValue;
    private readonly NameHashSet blockBeginTokens = new NameHashSet {new Name {Value = LeftParenthesis.ToString()}, new Name {Value = LeftAngle.ToString()}, new Name {Value = Pipe.ToString()}};
    private readonly NameHashSet blockEndTokens = new NameHashSet {new Name {Value = RightParenthesis.ToString()}, new Name {Value = RightAngle.ToString()}};
    private readonly Name pipeName = new Name {Value = Pipe.ToString()};
    private readonly Words putWords = new Words();
    private readonly Words setWords = new Words();
    private readonly Stack stack = new Stack();
    private readonly CharHashSet stringStopCharacters = new CharHashSet {Eof, Quote, LeftParenthesis, RightParenthesis, LeftAngle, RightAngle, Pipe};
    private readonly CharHashSet tokenStopCharacters = new CharHashSet {Eof, Quote, LeftParenthesis, RightParenthesis, LeftAngle, RightAngle, Pipe, Whitespace};
    private static string BaseDirectory { get; set; }

    private Runtime(string baseDirectory)
    {
      BaseDirectory = baseDirectory;
      setWords.Add(new Name {Value = "trace"}, new Action(Trace));
      setWords.Add(new Name {Value = "break"}, new Action(Break));
      setWords.Add(new Name {Value = "invoke"}, new Action(Invoke));
      setWords.Add(new Name {Value = ")"}, new Action(DoNothing));
      setWords.Add(new Name {Value = ">"}, new Action(Execute));
      setWords.Add(new Name {Value = "execute"}, new Action(Execute));
      setWords.Add(new Name {Value = "|"}, new Action(Put));
      setWords.Add(new Name {Value = "ones-complement"}, UnaryAction(ExpressionType.OnesComplement));
      setWords.Add(new Name {Value = "equal"}, BinaryAction(ExpressionType.Equal));
      setWords.Add(new Name {Value = "not-equal"}, BinaryAction(ExpressionType.NotEqual));
      setWords.Add(new Name {Value = "less-or-equal"}, BinaryAction(ExpressionType.LessThanOrEqual));
      setWords.Add(new Name {Value = "less"}, BinaryAction(ExpressionType.LessThan));
      setWords.Add(new Name {Value = "greater-or-equal"}, BinaryAction(ExpressionType.GreaterThanOrEqual));
      setWords.Add(new Name {Value = "greater"}, BinaryAction(ExpressionType.GreaterThan));
      setWords.Add(new Name {Value = "add"}, BinaryAction(ExpressionType.Add));
      setWords.Add(new Name {Value = "subtract"}, BinaryAction(ExpressionType.Subtract));
      setWords.Add(new Name {Value = "multiply"}, BinaryAction(ExpressionType.Multiply));
      setWords.Add(new Name {Value = "divide"}, BinaryAction(ExpressionType.Divide));
      setWords.Add(new Name {Value = "put"}, new Action(Put));
      setWords.Add(new Name {Value = "set"}, new Action(Set));
      setWords.Add(new Name {Value = "get"}, new Action(Get));
      setWords.Add(new Name {Value = "if"}, new Action(If));
      setWords.Add(new Name {Value = "while"}, new Action(While));
      setWords.Add(new Name {Value = "evaluate"}, new Action(Evaluate));
      setWords.Add(new Name {Value = "length"}, new Action(Length));
      setWords.Add(new Name {Value = "split"}, new Action(Split));
      setWords.Add(new Name {Value = "evaluate-and-split"}, new Action(EvaluateAndSplit));
      setWords.Add(new Name {Value = "join"}, new Action(Join));
      setWords.Add(new Name {Value = "cast"}, new Action(Cast));
      setWords.Add(new Name {Value = "create-event-handler"}, new Action(CreateEventHandler));
      setWords.Add(new Name {Value = "Runtime"}, typeof(Runtime));
      setWords.Add(new Name {Value = "runtime"}, this);
      setWords.Add(new Name {Value = "show"}, new Action(Show));
      setWords.Add(new Name {Value = "hello"}, new Action(Hello));
      setWords.Add(new Name {Value = "to-name"}, new Action(ToName));
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

    private static void Break()
    {
      Debugger.Break();
    }

    private static string Code(string filename)
    {
      return File.ReadAllText(BaseDirectory + "/" + filename);
    }

    private static void DoNothing() { }

    private static string GetToken(Characters characters, CharHashSet stopCharacters)
    {
      string result = "";
      while (!stopCharacters.Contains(NextCharacter(characters)))
      {
        result += characters.Dequeue();
      }
      return result;
    }

    private static void Hello()
    {
      MessageBox.Show("Hello!");
    }

    private static char NextCharacter(Characters characters)
    {
      char result = (0 < characters.Count) ? characters.Peek() : char.MinValue;
      return result == Eof ? result : char.IsWhiteSpace(result) ? Whitespace : result;
    }

    private static object ToObject(object token)
    {
      if (int.TryParse(token.ToString(), out int intValue))
      {
        return intValue;
      }
      if (double.TryParse(token.ToString(), out double doubleValue))
      {
        return doubleValue;
      }
      return new Name {Value = token.ToString()};
    }

    private Action BinaryAction(ExpressionType expressionType)
    {
      Type objectType = typeof(object);
      ParameterExpression parameterA = Expression.Parameter(objectType);
      ParameterExpression parameterB = Expression.Parameter(objectType);
      CSharpArgumentInfo argumentInfo = CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null);
      CSharpArgumentInfo[] argumentInfos = {argumentInfo, argumentInfo};
      CallSiteBinder binder = Binder.BinaryOperation(CSharpBinderFlags.None, expressionType, objectType, argumentInfos);
      DynamicExpression expression = Expression.Dynamic(binder, objectType, parameterB, parameterA);
      LambdaExpression lambda = Expression.Lambda(expression, parameterA, parameterB);
      Delegate function = lambda.Compile();
      return delegate { Push(function.DynamicInvoke(Pop(), Pop())); };
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
      object testItem = Pop();
      string memberName = ((Name) testItem).Value;
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
      foreach (Name currentKey in ((Items) Pop()).Select(currentItem => (Name) currentItem))
      {
        Push(putWords.ContainsKey(currentKey) ? putWords[currentKey] : setWords[currentKey]);
      }
    }

    private Items GetItems(Tokens tokens, out object lastToken)
    {
      lastToken = "";
      Items result = new Items();
      while (0 < tokens.Count)
      {
        object currentToken = tokens.Dequeue();
        lastToken = currentToken;
        if (blockBeginTokens.Contains(currentToken))
        {
          OnBlockBegin(currentToken);
          result.Add(GetItems(tokens, out object currentLastToken));
          result.Add(currentLastToken);
        }
        else if (blockEndTokens.Contains(currentToken))
        {
          OnBlockEnd(currentToken);
          break;
        }
        else
        {
          result.Add(currentToken);
        }
      }
      return result;
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
            characters.Dequeue();
            result.Enqueue(ToObject(nextCharacter));
            break;
          default:
            string currentToken = GetToken(characters, tokenStopCharacters);
            if (currentToken == PragmaToken)
            {
              HandlePragma(result);
            }
            else
            {
              result.Enqueue(ToObject(currentToken));
            }
            break;
        }
      }
      return result;
    }

    private void HandlePragma(Tokens tokens)
    {
      string pragma = ((Name) tokens.Dequeue()).Value;
      switch (pragma)
      {
        case LoadFilePragma:
          Run(((Name) tokens.Dequeue()).Value);
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
      object memberNameObject = Pop();
      string memberName = ((Name) memberNameObject).Value;
      object typeNameObject = Pop();
      string typeName = ((Name) typeNameObject).Value;
      object assemblyNameObject = Pop();
      string assemblyName = (string) assemblyNameObject;
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

    private void OnBlockBegin(object token)
    {
      if (token is Name name && name.Equals(pipeName))
      {
        blockBeginTokens.Remove(pipeName);
        blockEndTokens.Add(pipeName);
      }
    }

    private void OnBlockEnd(object token)
    {
      if (token is Name name && name.Equals(pipeName))
      {
        blockEndTokens.Remove(pipeName);
        blockBeginTokens.Add(pipeName);
      }
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

    private void Process(object item)
    {
      switch (TryGetWord(item, out object word))
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

    private void Push(object item)
    {
      stack.Push(item);
    }

    private void Put()
    {
      foreach (Name currentItem in Enumerable.Reverse((Items) Pop()))
      {
        putWords[currentItem] = Pop();
      }
    }

    private void Run(string filename)
    {
      Tokens tokens = GetTokens(Code(filename));
      Items items = GetItems(tokens, out _);
      Evaluate(items);
    }

    private void Set()
    {
      object item = Pop();
      Items items = (Items) item;
      foreach (Name currentItem in Enumerable.Reverse(items))
      {
        setWords[currentItem] = Pop();
      }
    }

    private void Show()
    {
      MessageBox.Show(Pop().ToString());
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

    private void ToName()
    {
      Push(new Name {Value = Pop().ToString()});
    }

    private void Trace()
    {
      object item = stack.Peek();
      MessageBox.Show(item == null ? "(empty)" : $"{item} ({item.GetType().Name})");
    }

    private WordKind TryGetWord(object item, out object word)
    {
      if (item is Name key)
      {
        if (putWords.TryGetValue(key, out word))
        {
          return WordKind.Put;
        }
        if (setWords.TryGetValue(key, out word))
        {
          return WordKind.Set;
        }
      }
      word = null;
      return WordKind.None;
    }

    private Action UnaryAction(ExpressionType expressionType)
    {
      Type objectType = typeof(object);
      ParameterExpression parameter = Expression.Parameter(objectType);
      CSharpArgumentInfo argumentInfo = CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null);
      CSharpArgumentInfo[] argumentInfos = {argumentInfo};
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