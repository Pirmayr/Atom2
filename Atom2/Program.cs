using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CSharp.RuntimeBinder;
using Binder = Microsoft.CSharp.RuntimeBinder.Binder;

#pragma warning disable 618

namespace Atom2
{
  using Tokens = Queue<object>;
  using CharHashSet = HashSet<char>;
  using Characters = Queue<char>;
  using Stack = Stack<object>;
  using Items = List<object>;
  using Words = ScopedDictionary<string, object>;
  using Parameters = ScopedDictionary<string, object>;
  using WordDescriptions = Dictionary<string, WordDescription>;

  public sealed class ScopedDictionary<TK, TV>
  {
    private sealed class Scopes : Stack<Dictionary<TK, TV>> { }

    private readonly Scopes scopes = new Scopes();

    public ScopedDictionary()
    {
      EnterScope();
    }

    public void Add(TK key, TV value)
    {
      scopes.Peek().Add(key, value);
    }

    public bool ContainsKey(TK key)
    {
      foreach (Dictionary<TK, TV> currentScope in scopes)
      {
        if (currentScope.ContainsKey(key))
        {
          return true;
        }
      }
      return false;
    }

    public void EnterScope()
    {
      scopes.Push(new Dictionary<TK, TV>());
    }

    public void LeaveScope()
    {
      scopes.Pop();
    }

    public bool TryGetValue(TK key, out TV value)
    {
      foreach (Dictionary<TK, TV> currentScope in scopes)
      {
        if (currentScope.TryGetValue(key, out value))
        {
          return true;
        }
      }
      value = default(TV);
      return false;
    }

    public TV this[TK key]
    {
      get => TryGetValue(key, out TV result) ? result : default(TV);
      set => scopes.Peek()[key] = value;
    }
  }

  public sealed class Runtime
  {
    private const string ActionNameEmptyAction = "empty-action";
    private const string ActionNameNoAction = "no-action";
    private const char Eof = char.MinValue;
    private const string LeftDelimiter = "left-delimiter";
    private const string RightDelimiter = "right-delimiter";
    private const char Whitespace = char.MaxValue;
    private readonly Parameters parameters = new Parameters();
    private readonly Stack stack = new Stack();
    private readonly CharHashSet stringStopCharacters = new CharHashSet { Eof, '"' };
    private readonly CharHashSet tokenStopCharacters = new CharHashSet { Whitespace, Eof, '"' };
    private readonly WordDescriptions wordDescriptions = new WordDescriptions();
    private readonly Words words = new Words();
    private static string BaseDirectory { get; set; }

    private Runtime(string baseDirectory)
    {
      BaseDirectory = baseDirectory;
      words.Add("invoke", new Action(Invoke));
      words.Add("execute", new Action(Execute));
      words.Add("equal", BinaryAction(ExpressionType.Equal));
      words.Add("not-equal", BinaryAction(ExpressionType.NotEqual));
      words.Add("less-or-equal", BinaryAction(ExpressionType.LessThanOrEqual));
      words.Add("less", BinaryAction(ExpressionType.LessThan));
      words.Add("greater-or-equal", BinaryAction(ExpressionType.GreaterThanOrEqual));
      words.Add("greater", BinaryAction(ExpressionType.GreaterThan));
      words.Add("add", BinaryAction(ExpressionType.Add));
      words.Add("subtract", BinaryAction(ExpressionType.Subtract));
      words.Add("multiply", BinaryAction(ExpressionType.Multiply));
      words.Add("divide", BinaryAction(ExpressionType.Divide));
      words.Add("put", new Action(Put));
      words.Add("set", new Action(Set));
      words.Add("get", new Action(Get));
      words.Add("if", new Action(If));
      words.Add("while", new Action(While));
      words.Add("evaluate", new Action(Evaluate));
      words.Add("length", new Action(Length));
      words.Add("evaluate-and-split", new Action(EvaluateAndSplit));
      words.Add("enter-scope", new Action(EnterScope));
      words.Add("leave-scope", new Action(LeaveScope));
      words.Add("make-list", new Action(MakeList));
      words.Add("cast", new Action(Cast));
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

    private static string Code(string filename)
    {
      return File.ReadAllText(BaseDirectory + "/" + filename);
    }

    private static void EmptyAction() { }

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

    private void AddWordDescription(string token, string name, ActionKind actionKind, params Action[] actions)
    {
      WordDescription wordDescription = new WordDescription(name, actionKind, actions);
      wordDescriptions.Add(token, wordDescription);
      switch (wordDescription.actionKind)
      {
        case ActionKind.Normal:
          words.Add(wordDescription.name, wordDescription.NormalAction);
          break;
        case ActionKind.LeftItemsDelimiter:
        case ActionKind.RightItemsDelimiter:
          Action preAction = wordDescription.PreAction;
          if (preAction != null)
          {
            words.Add(wordDescription.PreName, preAction);
          }
          Action postAction = wordDescription.PostAction;
          if (postAction != null)
          {
            words.Add(wordDescription.PostName, postAction);
          }
          break;
      }
      tokenStopCharacters.Add(token.First());
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
      return delegate { Push(function.DynamicInvoke(stack.Pop(), stack.Pop())); };
    }

    private void Cast()
    {
      Type type = (Type) stack.Pop();
      object instance = stack.Pop();
      ParameterExpression parameter = Expression.Parameter(instance.GetType());
      stack.Push(Expression.Lambda(Expression.Convert(parameter, type), parameter).Compile().DynamicInvoke(instance));
    }

    private void EnterScope()
    {
      words.EnterScope();
    }

    private void Evaluate(object unit)
    {
      if (unit is Items list)
      {
        list.ForEach(Process);
        return;
      }
      Process(unit);
    }

    private void Evaluate()
    {
      Evaluate(stack.Pop());
    }

    private void EvaluateAndSplit()
    {
      object items = stack.Pop();
      int stackLength = stack.Count;
      Evaluate(items);
      Push(stack.Count - stackLength);
    }

    private void Execute()
    {
      int argumentsCount = (int) stack.Pop();
      string memberName = (string) stack.Pop();
      object[] arguments = Pop(argumentsCount - 1);
      object typeOrTarget = stack.Pop();
      bool isType = typeOrTarget is Type;
      Type type = isType ? (Type) typeOrTarget : typeOrTarget.GetType();
      object target = isType ? null : typeOrTarget;
      BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic;
      bool hasReturnValue = false;
      switch (memberName)
      {
        case "initialize":
          memberName = ".ctor";
          hasReturnValue = false;
          bindingFlags |= BindingFlags.Static;
          break;
        case "new":
          memberName = ".ctor";
          hasReturnValue = true;
          bindingFlags |= BindingFlags.Instance;
          break;
        default:
          bindingFlags |= BindingFlags.Static | BindingFlags.Instance;
          break;
      }
      MemberInfo member = type.GetMember(memberName, bindingFlags | BindingFlags.Static).FirstOrDefault();
      if (member != null)
      {
        switch (member)
        {
          case ConstructorInfo _:
            bindingFlags |= BindingFlags.CreateInstance;
            break;
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
        }
      }
      object invokeResult = type.InvokeMember(memberName, bindingFlags, null, target, arguments);
      if (hasReturnValue)
      {
        stack.Push(invokeResult);
      }

    }

    private void Get()
    {
      foreach (string currentKey in from currentItem in (Items) stack.Pop() select currentItem.ToString())
      {
        Push(parameters.ContainsKey(currentKey) ? parameters[currentKey] : words[currentKey]);
      }
    }

    private Items GetItems(Tokens tokens, string firstToken, out string lastToken)
    {
      lastToken = null;
      Items result = new Items();
      if (firstToken != null && wordDescriptions.TryGetValue(firstToken, out WordDescription wordDescription) && wordDescription.IsValidPostLeftDelimiter)
      {
        result.Add(wordDescription.PostName);
      }
      while (0 < tokens.Count)
      {
        string currentToken = tokens.Dequeue().ToString();
        lastToken = currentToken;
        if (wordDescriptions.TryGetValue(currentToken, out WordDescription currentLeftDescription) && currentLeftDescription.IsLeftDelimiter)
        {
          if (currentLeftDescription.IsValidPreLeftDelimiter)
          {
            result.Add(currentLeftDescription.PreName);
          }
          result.Add(GetItems(tokens, currentToken, out string currentLastToken));
          if (wordDescriptions.TryGetValue(currentLastToken, out WordDescription currentRightDescription) && currentRightDescription.IsValidPostRightDelimiter)
          {
            result.Add(currentRightDescription.PostName);
          }
        }
        else if (wordDescriptions.TryGetValue(currentToken, out WordDescription currentRightDescription) && currentRightDescription.IsRightDelimiter)
        {
          if (currentRightDescription.IsValidPreRightDelimiter)
          {
            result.Add(currentRightDescription.PreName);
          }
          break;
        }
        else if (wordDescriptions.TryGetValue(currentToken, out WordDescription currentNormalTokenDescription) && currentNormalTokenDescription.IsValidNormalToken)
        {
          result.Add(currentNormalTokenDescription.NormalTokenName);
        }
        else
        {
          result.Add(ToObject(currentToken));
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
        if (nextCharacter == Whitespace)
        {
          characters.Dequeue();
        }
        else if (nextCharacter == '"')
        {
          characters.Dequeue();
          result.Enqueue(GetToken(characters, stringStopCharacters));
          characters.Dequeue();
        }
        else if (wordDescriptions.TryGetValue(nextCharacter.ToString(), out WordDescription _))
        {
          result.Enqueue(nextCharacter.ToString());
          characters.Dequeue();
        }
        else
        {
          string currentToken = GetToken(characters, tokenStopCharacters);
          if (currentToken == "pragma")
          {
            HandlePragma(result);
          }
          else
          {
            result.Enqueue(ToObject(currentToken));
          }
        }
      }
      return result;
    }

    private void HandlePragma(Tokens tokens)
    {
      string pragma = tokens.Dequeue().ToString();
      switch (pragma)
      {
        case "single-character-token":
        case LeftDelimiter:
        case RightDelimiter:
          ActionKind actionKind = ActionKind.Normal;
          switch (pragma)
          {
            case LeftDelimiter:
              actionKind = ActionKind.LeftItemsDelimiter;
              break;
            case RightDelimiter:
              actionKind = ActionKind.RightItemsDelimiter;
              break;
          }
          string token = tokens.Dequeue().ToString();
          string name = tokens.Dequeue().ToString();
          int actionsCount = (int) tokens.Dequeue();
          Action[] actions = new Action[actionsCount];
          for (int i = 0; i < actionsCount; ++i)
          {
            switch (tokens.Dequeue().ToString())
            {
              case ActionNameNoAction:
                actions[i] = null;
                break;
              case ActionNameEmptyAction:
                actions[i] = EmptyAction;
                break;
            }
          }
          AddWordDescription(token, name, actionKind, actions);
          break;
        case "load-file":
          Run(tokens.Dequeue().ToString());
          break;
      }
    }

    private void If()
    {
      object condition = stack.Pop();
      object body = stack.Pop();
      Evaluate(condition);
      if ((dynamic) stack.Pop())
      {
        Evaluate(body);
      }
    }

    private void Invoke()
    {
      BindingFlags memberKind = (BindingFlags) stack.Pop();
      BindingFlags memberType = (BindingFlags) stack.Pop();
      string memberName = (string) stack.Pop();
      string typeName = (string) stack.Pop();
      string assemblyName = (string) stack.Pop();
      int argumentsCount = (int) stack.Pop();
      object[] arguments = Pop(argumentsCount);
      Assembly assembly = Assembly.LoadWithPartialName(assemblyName);
      Type type = assembly.GetType(typeName);
      BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | memberKind | memberType;
      bool isInstance = memberType.HasFlag(BindingFlags.Instance);
      bool isConstructor = memberKind.HasFlag(BindingFlags.CreateInstance);
      object target = isInstance && !isConstructor ? stack.Pop() : null;
      object result = type.InvokeMember(memberName, bindingFlags, null, target, arguments);
      Push(result);
    }

    private void LeaveScope()
    {
      words.LeaveScope();
    }

    private void Length()
    {
      Push(((Items) stack.Pop()).Count);
    }

    private void MakeList()
    {
      stack.Push(new Items(Pop((int) stack.Pop())));
    }

    private object[] Pop(int count)
    {
      object[] result = new object[count];
      for (int i = count - 1; 0 <= i; --i)
      {
        result[i] = stack.Pop();
      }
      return result;
    }

    private void Process(object unit)
    {
      if (words.TryGetValue(unit.ToString(), out object word))
      {
        if (word is Action action)
        {
          action.Invoke();
          return;
        }
        parameters.EnterScope();
        Evaluate(word);
        parameters.LeaveScope();
        return;
      }
      Push(unit);
    }

    private void Push(object item)
    {
      stack.Push(item);
    }

    private void Put()
    {
      foreach (object currentItem in Enumerable.Reverse((Items) stack.Pop()))
      {
        parameters[currentItem.ToString()] = stack.Pop();
      }
    }

    private void Run(string filename)
    {
      Evaluate(GetItems(GetTokens(Code(filename)), null, out _));
    }

    private void Set()
    {
      foreach (object currentItem in Enumerable.Reverse((Items) stack.Pop()))
      {
        words[currentItem.ToString()] = stack.Pop();
      }
    }

    private void While()
    {
      object condition = stack.Pop();
      object body = stack.Pop();
      Evaluate(condition);
      while ((dynamic) stack.Pop())
      {
        Evaluate(body);
        Evaluate(condition);
      }
    }
  }

  public enum ActionKind
  {
    Normal,
    LeftItemsDelimiter,
    RightItemsDelimiter
  }

  public sealed class WordDescription
  {
    public readonly ActionKind actionKind;
    public readonly string name;
    private readonly Action[] actions;
    public bool IsLeftDelimiter => actionKind == ActionKind.LeftItemsDelimiter;
    public bool IsRightDelimiter => actionKind == ActionKind.RightItemsDelimiter;
    public bool IsValidNormalToken => IsNormalToken && NormalAction != null;
    public bool IsValidPostLeftDelimiter => IsLeftDelimiter && PostAction != null;
    public bool IsValidPostRightDelimiter => IsRightDelimiter && PostAction != null;
    public bool IsValidPreLeftDelimiter => IsLeftDelimiter && PreAction != null;
    public bool IsValidPreRightDelimiter => IsRightDelimiter && PreAction != null;
    public Action NormalAction => actions[0];
    public string NormalTokenName => name;
    public Action PostAction => actions[1];
    public string PostName => "post-" + name;
    public Action PreAction => actions[0];
    public string PreName => "pre-" + name;
    private bool IsNormalToken => actionKind == ActionKind.Normal;

    public WordDescription(string name, ActionKind actionKind, params Action[] actions)
    {
      this.name = name;
      this.actionKind = actionKind;
      this.actions = actions;
    }
  }
}