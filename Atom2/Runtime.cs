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
using Application = Eto.Forms.Application;

#pragma warning disable 618

namespace Atom2
{
  public class CallEnvironment
  {
    public Items Items { get; set; }
    public object CurrentItem { get; set; }
    public Words.Scope Scope { get; set; }
  }

  public class CallEnvironments : Stack<CallEnvironment> { }

  public sealed class Runtime
  {
    public event Action Breaking;

    private const char Apostrophe = '\'';
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
    private readonly Name apostropheName = new Name {Value = Apostrophe.ToString()};
    private readonly NameHashSet blockBeginTokens;
    private readonly NameHashSet blockEndTokens;
    private readonly Name pipeName = new Name {Value = Pipe.ToString()};
    private readonly Words putWords = new Words();
    private readonly Words setWords = new Words();
    private readonly Stack stack = new Stack();
    private readonly CharHashSet stringStopCharacters = new CharHashSet {Eof, Quote};
    private readonly CharHashSet tokenStopCharacters = new CharHashSet {Eof, Quote, Whitespace, LeftParenthesis, RightParenthesis, LeftAngle, RightAngle, Pipe, Apostrophe};

    public CallEnvironments CallEnvironments { get; } = new CallEnvironments();

    public Items CurrentRootItems { get; private set; }

    private Application Application { get; set; }

    private static string BaseDirectory { get; set; }

    public Runtime(Application application, string baseDirectory)
    {
      Application = application;

      blockBeginTokens = NewNameHashSet(LeftParenthesis, LeftAngle, Pipe, Apostrophe);
      blockEndTokens = NewNameHashSet(RightParenthesis, RightAngle);
      BaseDirectory = baseDirectory;
      setWords.Add(new Name {Value = "trace"}, new Action(Trace));
      setWords.Add(new Name {Value = "break"}, new Action(Break));
      setWords.Add(new Name {Value = "invoke"}, new Action(Invoke));
      setWords.Add(new Name {Value = ")"}, new Action(DoNothing));
      setWords.Add(new Name {Value = ">"}, new Action(Execute));
      setWords.Add(new Name {Value = "execute"}, new Action(Execute));
      setWords.Add(new Name {Value = "|"}, new Action(Put));
      setWords.Add(new Name {Value = "\'"}, new Action(DoNothing));
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

    public bool Run(string codeOrPath, out Exception result)
    {
      try
      {
        CurrentRootItems = GetItems(GetTokens(Code(codeOrPath)), out _);
        CallEnvironments.Push(new CallEnvironment { Items = CurrentRootItems, Scope = putWords.CurrentScope });
        Evaluate(CurrentRootItems);
        CallEnvironments.Pop();
        result = null;
        return true;
      }
      catch (Exception exception)
      {
        result = exception;
        return false;
      }
    }

    private void Break()
    {
      Breaking();
    }

    public static string Code(string codeOrFilename)
    {
      string path = BaseDirectory + "/" + codeOrFilename;
      return File.Exists(path) ? File.ReadAllText(path) : codeOrFilename;
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

    private static NameHashSet NewNameHashSet(params object[] arguments)
    {
      NameHashSet result = new NameHashSet();
      foreach (object currentArgument in arguments)
      {
        result.Add(new Name {Value = currentArgument.ToString()});
      }
      return result;
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
      if (item is Items items)
      {
        foreach (object currentItem in items)
        {
          CallEnvironments.Peek().CurrentItem = currentItem;
          Process(currentItem);
        }
        return;
      }
      CallEnvironments.Peek().CurrentItem = item;
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
      Application.Invoke(DoExecute);
    }

    private void DoExecute()
    {
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
          case Apostrophe:
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
          if (!Run(((Name) tokens.Dequeue()).Value, out Exception exception))
          {
            throw exception;
          }
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
      Application.Invoke(DoInvoke);
    }

    private void DoInvoke()
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
      if (token is Name name)
      {
        if (name.Equals(pipeName))
        {
          blockBeginTokens.Remove(pipeName);
          blockEndTokens.Add(pipeName);
        }
        else if (name.Equals(apostropheName))
        {
          blockBeginTokens.Remove(apostropheName);
          blockEndTokens.Add(apostropheName);
        }
      }
    }

    private void OnBlockEnd(object token)
    {
      if (token is Name name)
      {
        if (name.Equals(pipeName))
        {
          blockEndTokens.Remove(pipeName);
          blockBeginTokens.Add(pipeName);
        }
        else if (name.Equals(apostropheName))
        {
          blockEndTokens.Remove(apostropheName);
          blockBeginTokens.Add(apostropheName);
        }
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
          if (word is Items items)
          {
            putWords.EnterScope();
            CallEnvironments.Push(new CallEnvironment { Items = items, Scope = putWords.CurrentScope });
            Evaluate(items);
            CallEnvironments.Pop();
            putWords.LeaveScope();
            return;
          }
          Evaluate(word);
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

    private void Set()
    {
      foreach (Name currentItem in Enumerable.Reverse((Items) Pop()))
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
      object item = Pop();
      Items items = (Items) item;
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