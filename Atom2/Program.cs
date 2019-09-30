﻿using System;
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
using Characters = System.Collections.Generic.Queue<char>;
using Stack = System.Collections.Generic.Stack<object>;
using Items = System.Collections.Generic.List<object>;
using Words = Atom2.ScopedDictionary<string, object>;
using Parameters = Atom2.ScopedDictionary<string, object>;

#pragma warning disable 618

namespace Atom2
{
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

    // ReSharper disable once UnusedMember.Global
    public List<KeyValuePair<TK, TV>> ToList()
    {
      return scopes.Peek().ToList();
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
    private readonly CharHashSet stringStopCharacters = new CharHashSet {Eof, '"', '(', ')'};
    private readonly CharHashSet tokenStopCharacters = new CharHashSet {Eof, '"', '(', ')', Whitespace};
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
      words.Add("join", new Action(Join));
      words.Add("ones-complement", UnaryAction(ExpressionType.OnesComplement));
      words.Add("cast", new Action(Cast));
      words.Add("get-runtime", new Action(GetRuntime));
      words.Add("trace", new Action(Trace));
      words.Add("split", new Action(Split));
      words.Add("create-event-handler", new Action(CreateEventHandler));
      words.Add("break", new Action(Break));
      words.Add("call", new Action(Call));
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
      CSharpArgumentInfo[] argumentInfos = {argumentInfo, argumentInfo};
      CallSiteBinder binder = Binder.BinaryOperation(CSharpBinderFlags.None, expressionType, objectType, argumentInfos);
      DynamicExpression expression = Expression.Dynamic(binder, objectType, parameterB, parameterA);
      LambdaExpression lambda = Expression.Lambda(expression, parameterA, parameterB);
      Delegate function = lambda.Compile();
      return delegate { Push(function.DynamicInvoke(stack.Pop(), stack.Pop())); };
    }

    private void Call()
    {
      Items items = (Items) stack.Peek();
      string nameOrMethod = (string) items.LastOrDefault();
      if (TryGetWord(nameOrMethod, out object _))
      {
        Evaluate();
        return;
      }
      EvaluateAndSplit();
      Execute();
    }

    private void Cast()
    {
      Type type = (Type) stack.Pop();
      object instance = stack.Pop();
      stack.Push(Expression.Lambda(Expression.Convert(Expression.Constant(instance), type)).Compile().DynamicInvoke());
    }

    private void CreateEventHandler()
    {
      Items items = (Items) stack.Pop();
      EventHandler action = (sender, eventArguments) => EventHandler(items, sender, eventArguments);
      stack.Push(action);
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

    private void EventHandler(Items items, object sender, EventArgs eventArguments)
    {
      stack.Push(sender);
      stack.Push(eventArguments);
      Evaluate(items);
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
          memberName = "";
          bindingFlags |= BindingFlags.Static | BindingFlags.CreateInstance;
          break;
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

    private Items GetItems(Tokens tokens)
    {
      Items result = new Items();
      while (0 < tokens.Count)
      {
        string currentToken = tokens.Dequeue().ToString();
        if (currentToken.Equals("("))
        {
          result.Add(GetItems(tokens));
        }
        else if (currentToken.Equals(")"))
        {
          break;
        }
        else
        {
          result.Add(ToObject(currentToken));
        }
      }
      return result;
    }

    private void GetRuntime()
    {
      stack.Push(this);
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
        else if (nextCharacter == '(' || nextCharacter == ')')
        {
          characters.Dequeue();
          result.Enqueue(nextCharacter);
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

    private void Join()
    {
      stack.Push(new Items(Pop((int) stack.Pop())));
    }

    private void LeaveScope()
    {
      words.LeaveScope();
    }

    private void Length()
    {
      Push(((Items) stack.Pop()).Count);
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
      if (TryGetWord(unit.ToString(), out object word))
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
      Evaluate(GetItems(GetTokens(Code(filename))));
    }

    private void Set()
    {
      foreach (object currentItem in Enumerable.Reverse((Items) stack.Pop()))
      {
        words[currentItem.ToString()] = stack.Pop();
      }
    }

    private void Split()
    {
      Items items = (Items) stack.Pop();
      foreach (object currentItem in items)
      {
        stack.Push(currentItem);
      }
      Push(items.Count);
    }

    private void Trace()
    {
      MessageBox.Show(stack.Peek()?.ToString());
    }

    private bool TryGetWord(string key, out object word)
    {
      return parameters.TryGetValue(key, out word) || words.TryGetValue(key, out word);
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
      return delegate { Push(function.DynamicInvoke(stack.Pop())); };
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
}