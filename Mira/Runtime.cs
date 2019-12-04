namespace Mira
{
  using System;
  using System.Collections.Generic;
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
    private readonly Application application;
    private readonly string baseDirectory;
    private readonly Name executeName = new Name { Value = "execute" };
    private readonly StringHashSet pragmas = new StringHashSet { LoadFilePragma, ReferencePragma };
    private readonly Words putWords = new Words();
    private readonly SemaphoreSlim semaphore = new SemaphoreSlim(0);
    private readonly Words setWords = new Words();
    private readonly CharHashSet stringStopCharacters = new CharHashSet { Eof, Quote };
    private readonly CharHashSet tokenStopCharacters = new CharHashSet { Eof, Quote, Whitespace, LeftParenthesis, RightParenthesis };
    private string code;
    private bool evaluating;
    private bool stepping;
    public CallEnvironments CallEnvironments { get; } = new CallEnvironments();

    public string Code
    {
      get => code;
      set
      {
        code = value;
        Run(code, false);
      }
    }

    public Items CurrentRootItems { get; private set; }
    public bool Paused { get; private set; }
    public bool Running { get; private set; }
    public Stack Stack { get; } = new Stack();

    public Runtime(Application application, string baseDirectory)
    {
      this.application = application;
      this.baseDirectory = baseDirectory;
      Reference("mscorlib, Version=4.0.0.0, Culture=neutral", "System", "System.Collections", "System.Reflection");
      setWords.Add(new Name { Value = "break" }, new Action(Break));
      setWords.Add(new Name { Value = "execute" }, new Action(Execute));
      setWords.Add(new Name { Value = "put" }, new Action(Put));
      setWords.Add(new Name { Value = "runtime" }, this);
      setWords.Add(new Name { Value = "set" }, new Action(Set));
    }

    public void Continue(bool step)
    {
      stepping = step;
      semaphore.Release();
    }

    public void Run()
    {
      Task.Factory.StartNew(() => Run(Code, true));
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
      Paused = true;
      Invoke(RaiseBreaking);
      semaphore.Wait();
      Paused = false;
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

    private Delegate CreateDelegate(Type delegateType, Items delegateCode)
    {
      return CreateDelegate(delegateType, null, null, delegateCode);
    }

    private Delegate CreateDelegate(IEnumerable<Type> parameterTypes, Type returnType, Items delegateCode)
    {
      return CreateDelegate(typeof(void), parameterTypes, returnType, delegateCode);
    }

    private Delegate CreateDelegate(Type delegateType, IEnumerable<Type> parameterTypes, Type returnType, Items delegateCode)
    {
      if (delegateType != typeof(void))
      {
        MethodInfo invokeMethod = delegateType.GetMethod("Invoke");
        parameterTypes = invokeMethod.GetParameters().Select(currentParameter => currentParameter.ParameterType).ToArray();
        returnType = invokeMethod.ReturnType;
      }
      Expression codeConstant = Expression.Constant(delegateCode);
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

    private void DoShow()
    {
      MessageBox.Show(Pop().ToString());
    }

    private void DoTerminating(Exception exception)
    {
      Running = false;
      Invoke(() => RaiseTerminating(exception));
    }

    private void Evaluate(object item)
    {
      if (evaluating)
      {
        Items items = item as Items ?? new Items(item);
        CallEnvironments.Push(new CallEnvironment { Items = items });
        foreach (object currentItem in items)
        {
          CallEnvironments.Peek().CurrentItem = currentItem;
          HandleStepping();
          switch (GetWordAndWordKind(currentItem, out object word))
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

    private string GetCode(string codeOrFilename)
    {
      string path = baseDirectory + "/" + codeOrFilename;
      return File.Exists(path) ? File.ReadAllText(path) : codeOrFilename;
    }

    private Items GetItems(Tokens tokens)
    {
      Items result = new Items();
      while (0 < tokens.Count)
      {
        object currentToken = tokens.Dequeue();
        if (currentToken.ToString() == LeftParenthesis.ToString())
        {
          result.Add(GetItems(tokens));
        }
        else if (currentToken.ToString() == RightParenthesis.ToString())
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

    private Tokens GetTokens(string input)
    {
      Tokens result = new Tokens();
      Tokens currentPragmaTokens = new Tokens();
      Tokens currentTokens = result;
      Characters characters = new Characters(input.ToCharArray());
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

    private void HandleStepping()
    {
      if (stepping)
      {
        stepping = false;
        Paused = true;
        Invoke(RaiseStepping);
        semaphore.Wait();
        Paused = false;
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
      return new object[count].Select(_ => Pop()).Reverse();
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
      Enumerable.Reverse((Items) Pop()).ToList().ForEach(currentItem => putWords[(Name) currentItem] = Pop());
    }

    private void RaiseBreaking()
    {
      Breaking?.Invoke();
    }

    private void RaiseOutputting()
    {
      Outputting?.Invoke(this, Pop().ToString());
    }

    private void RaiseStepping()
    {
      Stepping?.Invoke();
    }

    private void RaiseTerminating(Exception exception)
    {
      Terminating?.Invoke(this, exception);
    }

    private void Reference(string assemblyName, params string[] requestedNamespaces)
    {
      foreach (Type currentType in Assembly.Load(assemblyName).GetTypes().Where(currentType => requestedNamespaces.Any(currentNamespace => currentType.Namespace == currentNamespace)))
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
            Name currentName = new Name { Value = currentMember.Name };
            if (!setWords.ContainsKey(currentName))
            {
              setWords.Add(currentName, new Items { currentMember.Name, executeName });
            }
          }
        }
      }
    }

    private Exception Run(string codeOrPath, bool evaluate)
    {
      try
      {
        evaluating = evaluate;
        CurrentRootItems = GetItems(GetTokens(GetCode(codeOrPath)));
        if (evaluating)
        {
          Stack.Clear();
          CallEnvironments.Clear();
          Running = true;
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
    }

    private void Set()
    {
      Enumerable.Reverse((Items) Pop()).ToList().ForEach(currentItem => setWords[(Name) currentItem] = Pop());
    }

    private void Show()
    {
      Invoke(DoShow);
    }

    private void Split()
    {
      Items items = (Items) Pop();
      items.ForEach(currentItem => Push(currentItem));
      Push(items.Count);
    }

    private WordKind GetWordAndWordKind(object item, out object word)
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
  }
}