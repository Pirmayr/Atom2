namespace Atom2
{
  using System;
  using System.Collections.Generic;
  using System.Diagnostics.CodeAnalysis;
  using System.Globalization;
  using System.IO;
  using System.Linq;
  using System.Linq.Expressions;
  using System.Reflection;
  using System.Runtime.CompilerServices;
  using System.Threading;
  using System.Threading.Tasks;
  using Eto.Forms;

  using Microsoft.CSharp.RuntimeBinder;

  using Binder = Microsoft.CSharp.RuntimeBinder.Binder;

  public sealed class Runtime
  {
    private const char Eof = char.MinValue;
    private const char LeftParenthesis = '(';
    private const string LoadFilePragma = "loadFile";
    private const string NewMemberName = "new";
    private const string PragmaToken = "pragma";
    private const char Quote = '"';
    private const string ReferencePragma = "reference";
    private const char RightParenthesis = ')';
    private const char Whitespace = char.MaxValue;
    private readonly NameHashSet blockBeginTokens;
    private readonly NameHashSet blockEndTokens;
    private readonly Name executeName = new Name { Value = "execute" };
    private readonly StringHashSet pragmas = new StringHashSet { LoadFilePragma, ReferencePragma };
    private readonly Words putWords = new Words();
    private readonly Words setWords = new Words();
    private readonly CharHashSet stringStopCharacters = new CharHashSet { Eof, Quote };
    private readonly CharHashSet tokenStopCharacters = new CharHashSet { Eof, Quote, Whitespace, LeftParenthesis, RightParenthesis };
    private bool isInEvaluationMode;
    private readonly SemaphoreSlim semaphore = new SemaphoreSlim(0);
    private bool paused;
    private bool running;
    private bool stepMode;
    private string code;
    private readonly Application application;
    private readonly string baseDirectory;

    public CallEnvironments CallEnvironments { get; } = new CallEnvironments();
    public Items CurrentRootItems { get; private set; }
    public Stack Stack { get; } = new Stack();

    public string GetCode()
    {
      return code;
    }

    public void SetCode(string value)
    {
      code = value;
      Run(code, false);
    }

    public Runtime(Application application, string baseDirectory)
    {
      this.application = application;
      this.baseDirectory = baseDirectory;
      blockBeginTokens = NewNameHashSet(LeftParenthesis);
      blockEndTokens = NewNameHashSet(RightParenthesis);
      setWords.Add(new Name { Value = "trace" }, new Action(Trace));
      setWords.Add(new Name { Value = "output" }, new Action(Output));
      setWords.Add(new Name { Value = "show" }, new Action(Show));
      setWords.Add(new Name { Value = "break" }, new Action(Break));
      setWords.Add(new Name { Value = "execute" }, new Action(Execute));
      setWords.Add(new Name { Value = "put" }, new Action(Put));
      setWords.Add(new Name { Value = "set" }, new Action(Set));
      setWords.Add(new Name { Value = "get" }, new Action(Get));
      setWords.Add(new Name { Value = "if" }, new Action(If));
      setWords.Add(new Name { Value = "while" }, new Action(While));
      setWords.Add(new Name { Value = "evaluate" }, new Action(Evaluate));
      setWords.Add(new Name { Value = "createDelegate" }, new Action(CreateDelegate));
      setWords.Add(new Name { Value = "runtime" }, this);
      setWords.Add(new Name { Value = "makeOperation" }, new Action(MakeOperation));
      Reference("mscorlib, Version=4.0.0.0, Culture=neutral", "System", "System.Reflection");
      Reference("System.Core, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", "System.Linq.Expressions");
      Reference("System.Numerics, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089", "System.Numerics");
    }

    public string GetCode(string codeOrFilename)
    {
      string path = baseDirectory + "/" + codeOrFilename;
      return File.Exists(path) ? File.ReadAllText(path) : codeOrFilename;
    }

    private Exception Run(string codeOrPath, bool evaluate)
    {
      bool oldRunning = running;
      try
      {
        running = true;
        isInEvaluationMode = evaluate;
        CurrentRootItems = GetItems(GetTokens(GetCode(codeOrPath)));
        if (isInEvaluationMode)
        {
          Stack.Clear();
          CallEnvironments.Clear();
          Evaluate(CurrentRootItems);
          DoTerminating(null);
        }
        return null;
      }
      catch (Exception exception)
      {
        DoTerminating(exception);
        return exception;
      }
      finally
      {
        running = oldRunning;
      }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("Code Quality", "IDE0051:Remove unused private members", Justification = "<Pending>")]
    private void Split()
    {
      Items items = (Items) Pop();
      foreach (object currentItem in items)
      {
        Push(currentItem);
      }
      Push(items.Count);
    }

    private static string GetCall(Type type, string memberName, object[] arguments)
    {
      string typeName = type == null ? string.Empty : type.Name;
      string argumentsString = arguments == null ? string.Empty : string.Join(", ", arguments);
      return typeName + "." + memberName + "(" + argumentsString + ")";
    }

    private static string GetToken(Characters characters, CharHashSet stopCharacters)
    {
      string result = string.Empty;
      while (!stopCharacters.Contains(NextCharacter(characters)))
      {
        result += characters.Dequeue();
      }
      return result;
    }

    public void Run()
    {
      Task.Factory.StartNew(Run, GetCode());
    }

    private Exception Run(object code)
    {
      return Run((string) code, true);
    }

    private static NameHashSet NewNameHashSet(params object[] arguments)
    {
      NameHashSet result = new NameHashSet();
      foreach (object currentArgument in arguments)
      {
        result.Add(new Name { Value = currentArgument.ToString() });
      }
      return result;
    }

    private static char NextCharacter(Characters characters)
    {
      char result = 0 < characters.Count ? characters.Peek() : char.MinValue;
      return result == Eof ? result : char.IsWhiteSpace(result) ? Whitespace : result;
    }

    private static object ToObject(object token)
    {
      if (int.TryParse(token.ToString(), out int intValue))
      {
        return intValue;
      }
      if (double.TryParse(token.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double doubleValue))
      {
        return doubleValue;
      }
      return new Name { Value = token.ToString() };
    }

    private void Break()
    {
      paused = true;
      Invoke(RaiseBreaking);
      semaphore.Wait();
      paused = false;
    }

    private void RaiseBreaking()
    {
      Breaking?.Invoke();
    }

    private void CreateDelegate()
    {
      if (Peek() is Type)
      {
        Push(CreateDelegate((Type) Pop(), (Items) Pop()));
        return;
      }
      EvaluateAndSplit();
      Push(CreateDelegate(Pop((int) Pop()).Cast<Type>(), (Type) Pop(), (Items) Pop()));
    }

    private Delegate CreateDelegate(Type delegateType, Items code)
    {
      return CreateDelegate(delegateType, null, null, code);
    }

    private Delegate CreateDelegate(IEnumerable<Type> parameterTypes, Type returnType, Items code)
    {
      return CreateDelegate(typeof(void), parameterTypes, returnType, code);
    }

    [SuppressMessage("ReSharper", "PossibleNullReferenceException")]
    private Delegate CreateDelegate(Type delegateType, IEnumerable<Type> parameterTypes, Type returnType, Items code)
    {
      if (delegateType != typeof(void))
      {
        MethodInfo invokeMethod = delegateType.GetMethod("Invoke");
        parameterTypes = invokeMethod.GetParameters().Select(currentParameter => currentParameter.ParameterType).ToArray();
        returnType = invokeMethod.ReturnType;
      }
      Expression codeConstant = Expression.Constant(code);
      Expression thisConstant = Expression.Constant(this);
      MethodInfo pushMethod = ((Action<object>) Push).Method;
      MethodInfo popMethod = ((Func<object>) Pop).Method;
      MethodInfo evaluateMethod = ((Action<object>) Evaluate).Method;
      ParameterExpression[] parameters = parameterTypes.Select(Expression.Parameter).ToArray();
      List<Expression> statements = new List<Expression>();
      statements.AddRange(parameters.Select(currentParameter => Expression.Call(thisConstant, pushMethod, Expression.Convert(currentParameter, typeof(object)))));
      statements.Add(Expression.Call(thisConstant, evaluateMethod, codeConstant));
      if (returnType != typeof(void))
      {
        statements.Add(Expression.Convert(Expression.Call(thisConstant, popMethod), returnType));
      }
      return (delegateType == typeof(void) ? Expression.Lambda(Expression.Block(statements), parameters) : Expression.Lambda(delegateType, Expression.Block(statements), parameters)).Compile();
    }

    private Exception DoExecute(Type type, string memberName, BindingFlags bindingFlags, object target, object[] arguments, bool hasReturnValue)
    {
      try
      {
        object invokeResult = type.InvokeMember(memberName, bindingFlags, null, target, arguments);
        if (hasReturnValue)
        {
          Push(invokeResult);
        }
        return null;
      }
      catch (Exception exception)
      {
        return exception;
      }
    }

    private void RaiseTerminating(Exception exception)
    {
      Terminating?.Invoke(this, exception);
    }

    private void RaiseOutputting()
    {
      Outputting?.Invoke(this, Pop().ToString());
    }

    private void DoShow()
    {
      MessageBox.Show(Pop().ToString());
    }

    private void DoTrace()
    {
      Outputting?.Invoke(this, Stack.Peek().ToInformation());
    }

    private void Evaluate(object item)
    {
      if (isInEvaluationMode)
      {
        Items items = item.ToItems();
        CallEnvironments.Push(new CallEnvironment { Items = items });
        foreach (object currentItem in items)
        {
          CallEnvironments.Peek().CurrentItem = currentItem;
          HandleStepping();
          switch (TryGetWord(currentItem, out object word))
          {
            case WordKind.Set:
              switch (word)
              {
                case Action actionValue:
                  actionValue.Invoke();
                  break;
                case Items itemsValue:
                  putWords.EnterScope();
                  Evaluate(itemsValue);
                  putWords.LeaveScope();
                  break;
                default:
                  Evaluate(word);
                  break;
              }
              break;
            case WordKind.Put:
              Push(word);
              break;
            default:
              Push(currentItem);
              break;
          }
        }
        CallEnvironments.Pop();
      }
    }

    private void HandleStepping()
    {
      if (stepMode)
      {
        stepMode = false;
        paused = true;
        Invoke(RaiseStepping);
        semaphore.Wait();
        paused = false;
      }
    }

    private void RaiseStepping()
    {
      Stepping?.Invoke();
    }

    private void Evaluate()
    {
      Evaluate(Pop());
    }

    private void EvaluateAndSplit()
    {
      object items = Pop();
      int stackLength = Stack.Count;
      Evaluate(items);
      Push(Stack.Count - stackLength);
    }

    [SuppressMessage("ReSharper", "PatternAlwaysOfType")]
    private void Execute()
    {
      string memberName = (string) Pop();
      int argumentsCount = 0;
      if (Stack.Peek() is Items)
      {
        EvaluateAndSplit();
        argumentsCount = (int) Pop();
      }
      object[] arguments = Pop(argumentsCount).ToArray();
      object typeOrTarget;
      object typeOrTargetOrInstanceFlag = Pop();
      if (typeOrTargetOrInstanceFlag is bool forceInstance)
      {
        typeOrTarget = Pop();
      }
      else
      {
        forceInstance = false;
        typeOrTarget = typeOrTargetOrInstanceFlag;
      }
      bool isType = !forceInstance && typeOrTarget is Type;
      Type type = isType ? (Type) typeOrTarget : typeOrTarget.GetType();
      object target = isType ? null : typeOrTarget;
      BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic;
      bool hasReturnValue = false;
      switch (memberName)
      {
        case NewMemberName:
          memberName = string.Empty;
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
                bindingFlags |= hasReturnValue ? BindingFlags.GetField : BindingFlags.SetField;
                break;
              case PropertyInfo _:
                hasReturnValue = arguments.Length == 0;
                bindingFlags |= hasReturnValue ? BindingFlags.GetProperty : BindingFlags.SetProperty;
                break;
              case EventInfo eventInfo:
                memberName = eventInfo.AddMethod.Name;
                bindingFlags |= BindingFlags.InvokeMethod;
                break;
            }
          }
          break;
      }
      if (Invoke(() => DoExecute(type, memberName, bindingFlags, target, arguments, hasReturnValue)) is Exception exception)
      {
        throw exception;
      }
    }

    private void Get()
    {
      foreach (Name currentKey in ((Items) Pop()).Select(currentItem => (Name) currentItem))
      {
        Push(putWords.ContainsKey(currentKey) ? putWords[currentKey] : setWords[currentKey]);
      }
    }

    private Items GetItems(Tokens tokens)
    {
      Items result = new Items();
      while (0 < tokens.Count)
      {
        object currentToken = tokens.Dequeue();
        if (blockBeginTokens.Contains(currentToken))
        {
          result.Add(GetItems(tokens));
        }
        else if (blockEndTokens.Contains(currentToken))
        {
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
      Tokens currentPragmaTokens = new Tokens();
      Tokens currentTokens = result;
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
            currentTokens.Enqueue(GetToken(characters, stringStopCharacters));
            characters.Dequeue();
            break;
          case LeftParenthesis:
          case RightParenthesis:
            characters.Dequeue();
            currentTokens.Enqueue(ToObject(nextCharacter));
            break;
          default:
            string currentToken = GetToken(characters, tokenStopCharacters);
            if (pragmas.Contains(currentToken))
            {
              currentTokens = currentPragmaTokens;
              currentTokens.Enqueue(ToObject(currentToken));
            }
            else if (currentToken == PragmaToken)
            {
              currentTokens = result;
              HandlePragma(currentPragmaTokens);
              currentPragmaTokens.Clear();
            }
            else
            {
              currentTokens.Enqueue(ToObject(currentToken));
            }
            break;
        }
      }
      return result;
    }

    /// <summary>
    /// Hugo
    /// </summary>
    /// <param name="pragmaTokens"></param>
    [SuppressMessage("ReSharper", "PatternAlwaysOfType")]
    private void HandlePragma(Tokens pragmaTokens)
    {
      string pragma = ((Name) pragmaTokens.Dequeue()).Value;
      switch (pragma)
      {
        case LoadFilePragma:
          Evaluate(GetItems(GetTokens(GetCode(((Name) pragmaTokens.Dequeue()).Value))));
          break;
        case ReferencePragma:
          Reference((string) pragmaTokens.Dequeue(), (string) pragmaTokens.Dequeue());
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

    private void Invoke(Action action)
    {
      application.Invoke(action);
    }

    private T Invoke<T>(Func<T> function)
    {
      return application.Invoke(function);
    }

    private void DoTerminating(Exception exception)
    {
      running = false;
      Invoke(() => RaiseTerminating(exception));
    }

    [SuppressMessage("Code Quality", "IDE0051:Remove unused private members", Justification = "<Pending>")]
    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    private void Join()
    {
      Push(new Items(Pop(Convert.ToInt32(Pop()))));
    }

    private void MakeOperation()
    {
      object targetTypeOrParametersCount = Pop();
      if (targetTypeOrParametersCount is Type targetType)
      {
        Push(Operation(targetType));
        return;
      }
      Push(Operation(Pop(), (int) targetTypeOrParametersCount));
    }

    [SuppressMessage("ReSharper", "AssignNullToNotNullAttribute")]
    [SuppressMessage("ReSharper", "CoVariantArrayConversion")]
    [SuppressMessage("Style", "IDE0062:Make local function 'static'", Justification = "<Pending>")]
    private Action Operation(object targetTypeOrExpressionType, int parametersCount = 0)
    {
      CSharpArgumentInfo[] GetParametersAndArgumentInfos(int count, out ParameterExpression[] parameterExpressions)
      {
        CSharpArgumentInfo argumentInfo = CSharpArgumentInfo.Create(CSharpArgumentInfoFlags.None, null);
        parameterExpressions = new ParameterExpression[count];
        CSharpArgumentInfo[] argumentInfos = new CSharpArgumentInfo[count];
        for (int i = 0; i < count; ++i)
        {
          parameterExpressions[i] = Expression.Parameter(typeof(object));
          argumentInfos[i] = argumentInfo;
        }
        return argumentInfos;
      }

      ParameterExpression[] parameters = null;
      CallSiteBinder binder = null;
      if (targetTypeOrExpressionType is Type targetType)
      {
        parametersCount = 1;
        GetParametersAndArgumentInfos(parametersCount, out parameters);
        binder = Binder.Convert(CSharpBinderFlags.ConvertExplicit, targetType, typeof(object));
      }
      else
      {
        targetType = typeof(object);
        ExpressionType expressionType = (ExpressionType) targetTypeOrExpressionType;
        switch (parametersCount)
        {
          case 1:
            binder = Binder.UnaryOperation(CSharpBinderFlags.None, expressionType, typeof(object), GetParametersAndArgumentInfos(parametersCount, out parameters));
            break;
          case 2:
            binder = Binder.BinaryOperation(CSharpBinderFlags.None, expressionType, typeof(object), GetParametersAndArgumentInfos(parametersCount, out parameters));
            break;
        }
      }
      Delegate function = Expression.Lambda(Expression.Dynamic(binder, targetType, parameters), parameters).Compile();
      return () => Push(function.DynamicInvoke(Pop(parametersCount).ToArray()));
    }

    private void Output()
    {
      Invoke(RaiseOutputting);
    }

    private object Peek()
    {
      return Stack.Peek();
    }

    private IEnumerable<object> Pop(int count)
    {
      object[] result = new object[count];
      for (int i = count - 1; 0 <= i; --i)
      {
        result[i] = Stack.Pop();
      }
      return result;
    }

    private object Pop()
    {
      return Stack.Pop();
    }

    private void Push(object item)
    {
      Stack.Push(item);
    }

    private void Put()
    {
      foreach (Name currentItem in Enumerable.Reverse((Items) Pop()))
      {
        putWords[currentItem] = Pop();
      }
    }

    private void Reference(string assemblyName, params string[] requestedNamespaces)
    {
      HashSet<string> names = new HashSet<string>();
      foreach (Type currentType in Assembly.Load(assemblyName).GetTypes())
      {
        if (requestedNamespaces.Any(currentNamespace => currentNamespace == currentType.Namespace))
        {
          setWords[new Name { Value = currentType.Name }] = currentType;
          foreach (MemberInfo currentMember in currentType.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
          {
            bool accept = false;
            switch (currentMember)
            {
              case MethodInfo methodInfo:
                accept = !methodInfo.IsSpecialName;
                break;
              case FieldInfo fieldInfo:
                accept = !fieldInfo.IsSpecialName;
                break;
              case PropertyInfo propertyInfo:
                accept = !propertyInfo.IsSpecialName;
                break;
            }
            if (accept)
            {
              names.Add(currentMember.Name);
            }
          }
        }
      }
      foreach (string currentName in names)
      {
        Name newName = new Name { Value = currentName };
        if (!setWords.ContainsKey(newName))
        {
          setWords.Add(newName, new Items { currentName, executeName });
        }
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
      Invoke(DoShow);
    }

    private void Trace()
    {
      Invoke(DoTrace);
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

    public event Action Breaking;
    public event OutputtingEventHandler Outputting;
    public event Action Stepping;
    public event TerminatingEventHandler Terminating;

    public void Continue()
    {
      semaphore.Release();
    }

    public bool GetRunning()
    {
      return running;
    }

    public bool GetPaused()
    {
      return paused;
    }

    public void Step()
    {
      stepMode = true;
      semaphore.Release();
    }
  }
}