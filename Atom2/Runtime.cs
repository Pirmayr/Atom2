using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CSharp.RuntimeBinder;
using Binder = Microsoft.CSharp.RuntimeBinder.Binder;

// ReSharper disable ParameterOnlyUsedForPreconditionCheck.Local
#pragma warning disable 618

namespace Atom2
{
  using Tokens = Queue<object>;
  using CharHashSet = HashSet<char>;

  public sealed class Runtime
  {
    private sealed class Items : List<object>
    {
    }

    private sealed class Stack : Stack<object>
    {
      public object[] Pop(int count)
      {
        object[] result = new object[count];
        for (int i = count - 1; 0 <= i; --i)
        {
          result[i] = Pop();
        }
        return result;
      }
    }

    private sealed class WordDescription
    {
      public readonly ActionKind actionKind;
      public readonly string name;
      private readonly Action[] actions;
      public bool IsDelimiter => IsLeftDelimiter || IsRightDelimiter;
      public bool IsLeftDelimiter => actionKind == ActionKind.LeftItemsDelimiter;
      public bool IsRightDelimiter => actionKind == ActionKind.RightItemsDelimiter;
      public bool IsValidPostLeftDelimiter => IsLeftDelimiter && PostAction != null;
      public bool IsValidPostRightDelimiter => IsRightDelimiter && PostAction != null;
      public bool IsValidPreLeftDelimiter => IsLeftDelimiter && PreAction != null;
      public bool IsValidPreRightDelimiter => IsRightDelimiter && PreAction != null;
      public Action NormalAction => actions[0];
      public Action PostAction => actions[1];
      public string PostName => "post-" + name;
      public Action PreAction => actions[0];
      public string PreName => "pre-" + name;

      public WordDescription(string name, ActionKind actionKind, params Action[] actions)
      {
        this.name = name;
        this.actionKind = actionKind;
        this.actions = actions;
      }
    }

    private sealed class WordDescriptions : Dictionary<string, WordDescription>
    {
    }

    private sealed class Words : ScopedDictionary<string, object>
    {
    }

    private enum ActionKind
    {
      Normal,
      LeftItemsDelimiter,
      RightItemsDelimiter
    }

    private const char Eof = char.MinValue;
    private const char Whitespace = char.MaxValue;
    private readonly Stack stack = new Stack();
    private readonly CharHashSet stringStopCharacters = new CharHashSet {Eof, '"'};
    private readonly CharHashSet tokenStopCharacters = new CharHashSet {Eof, Whitespace, '(', ')', '[', ']', '{', '}', '<', '>', '"'};
    private readonly WordDescriptions wordDescriptions = new WordDescriptions();
    private readonly Words words = new Words();

    public Runtime()
    {
      wordDescriptions.Add("<", new WordDescription("left-angle", ActionKind.LeftItemsDelimiter, NoAction, NoAction));
      wordDescriptions.Add(">", new WordDescription("right-angle", ActionKind.RightItemsDelimiter, NoAction, NoAction));
      wordDescriptions.Add("{", new WordDescription("left-brace", ActionKind.LeftItemsDelimiter, NoAction, null));
      wordDescriptions.Add("}", new WordDescription("right-brace", ActionKind.RightItemsDelimiter, null, NoAction));
      wordDescriptions.Add("[", new WordDescription("left-bracket", ActionKind.LeftItemsDelimiter, NoAction, null));
      wordDescriptions.Add("]", new WordDescription("right-bracket", ActionKind.RightItemsDelimiter, null, NoAction));
      wordDescriptions.Add("(", new WordDescription("left-parenthesis", ActionKind.LeftItemsDelimiter, NoAction, null));
      wordDescriptions.Add(")", new WordDescription("right-parenthesis", ActionKind.RightItemsDelimiter, null, NoAction));
      foreach (WordDescription currentValue in wordDescriptions.Values)
      {
        switch (currentValue.actionKind)
        {
          case ActionKind.Normal:
            words.Add(currentValue.name, currentValue.NormalAction);
            break;
          case ActionKind.LeftItemsDelimiter:
          case ActionKind.RightItemsDelimiter:
            Action preAction = currentValue.PreAction;
            if (preAction != null)
            {
              words.Add(currentValue.PreName, preAction);
            }
            Action postAction = currentValue.PostAction;
            if (postAction != null)
            {
              words.Add(currentValue.PostName, postAction);
            }
            break;
        }
      }
      words.Add("invoke", new Action(Invoke));
      words.Add("equal", BinaryAction(ExpressionType.Equal));
      words.Add("not-equal", BinaryAction(ExpressionType.NotEqual));
      words.Add("less-or-equal", BinaryAction(ExpressionType.LessThanOrEqual));
      words.Add("less", BinaryAction(ExpressionType.LessThan));
      words.Add("add", BinaryAction(ExpressionType.Add));
      words.Add("subtract", BinaryAction(ExpressionType.Subtract));
      words.Add("set", new Action(Set));
      words.Add("get", new Action(Get));
      words.Add("if", new Action(If));
      words.Add("while", new Action(While));
      words.Add("evaluate", new Action(Evaluate));
      words.Add("length", new Action(Length));
      words.Add("split", new Action(Split));
      words.Add("enter-scope", new Action(EnterScope));
      words.Add("leave-scope", new Action(LeaveScope));
    }

    public void Run(string code)
    {
      Evaluate(GetItems(GetTokens(code), null, out _));
    }

    private static string GetToken(Queue<char> characters, HashSet<char> stopCharacters)
    {
      string result = "";
      while (!stopCharacters.Contains(NextCharacter(characters)))
      {
        result += characters.Dequeue();
      }
      return result;
    }

    private static char NextCharacter(Queue<char> characters)
    {
      char result = (0 < characters.Count) ? characters.Peek() : char.MinValue;
      return result == Eof ? result : char.IsWhiteSpace(result) ? Whitespace : result;
    }

    private static void NoAction()
    {
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
      return () => { stack.Push(function.DynamicInvoke(stack.Pop(), stack.Pop())); };
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

    private void Get()
    {
      foreach (object currentItem in (Items) stack.Pop())
      {
        stack.Push(words[currentItem.ToString()]);
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
        object currentTokenObject = tokens.Dequeue();
        string currentToken = currentTokenObject.ToString();
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
      Queue<char> characters = new Queue<char>(code.ToCharArray());
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
        else if (wordDescriptions.TryGetValue(nextCharacter.ToString(), out WordDescription wordDescription) && wordDescription.IsDelimiter)
        {
          result.Enqueue(nextCharacter.ToString());
          characters.Dequeue();
        }
        else
        {
          result.Enqueue(ToObject(GetToken(characters, tokenStopCharacters)));
        }
      }
      return result;
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
      object[] arguments = stack.Pop(argumentsCount);
      Assembly assembly = Assembly.LoadWithPartialName(assemblyName);
      Type type = assembly.GetType(typeName);
      BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | memberKind | memberType;
      bool isInstance = memberType.HasFlag(BindingFlags.Instance);
      bool isConstructor = memberKind.HasFlag(BindingFlags.CreateInstance);
      object target = isInstance && !isConstructor ? stack.Pop() : null;
      object result = type.InvokeMember(memberName, bindingFlags, null, target, arguments);
      stack.Push(result);
    }

    private void LeaveScope()
    {
      words.LeaveScope();
    }

    private void Length()
    {
      stack.Push(((Items) stack.Pop()).Count);
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
        Evaluate(word);
        return;
      }
      stack.Push(unit);
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
      object items = stack.Pop();
      int stackLength = stack.Count;
      Evaluate(items);
      stack.Push(stack.Count - stackLength);
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