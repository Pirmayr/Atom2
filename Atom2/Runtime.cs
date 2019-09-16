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
  using Tokens = Queue<string>;
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

    private sealed class Words : Dictionary<string, object>
    {
    }

    private const char Eof = char.MinValue;
    private const char Whitespace = char.MaxValue;
    private readonly Stack stack = new Stack();
    private readonly CharHashSet stringStopCharacters = new CharHashSet {Eof, '\''};
    private readonly CharHashSet tokenStopCharacters = new CharHashSet {Eof, Whitespace, '(', ')', '\''};
    private readonly Words words = new Words();

    public Runtime()
    {
      words.Add("invoke", new Action(Invoke));
      words.Add("not-equal", BinaryAction(ExpressionType.NotEqual));
      words.Add("less-or-equal", BinaryAction(ExpressionType.LessThanOrEqual));
      words.Add("add", BinaryAction(ExpressionType.Add));
      words.Add("subtract", BinaryAction(ExpressionType.Subtract));
      words.Add("set", new Action(Set));
      words.Add("get", new Action(Get));
      words.Add("if", new Action(If));
      words.Add("while", new Action(While));
      words.Add("evaluate", new Action(Evaluate));
    }

    public void Run(string code)
    {
      Evaluate(GetItems(GetTokens(code)));
    }

    private static Items GetItems(Tokens tokens)
    {
      Items result = new Items();
      while (0 < tokens.Count)
      {
        string currentToken = tokens.Dequeue();
        if (currentToken == "(")
        {
          result.Add(GetItems(tokens));
        }
        else if (currentToken == ")")
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

    private Tokens GetTokens(string code)
    {
      Tokens result = new Tokens();
      Queue<char> characters = new Queue<char>(code.ToCharArray());
      for (char nextCharacter = NextCharacter(characters); nextCharacter != Eof; nextCharacter = NextCharacter(characters))
      {
        switch (nextCharacter)
        {
          case '(':
            result.Enqueue("(");
            characters.Dequeue();
            break;
          case ')':
            result.Enqueue(")");
            characters.Dequeue();
            break;
          case '\'':
            characters.Dequeue();
            result.Enqueue(GetToken(characters, stringStopCharacters));
            characters.Dequeue();
            break;
          default:
            if (nextCharacter == Whitespace)
            {
              characters.Dequeue();
            }
            else
            {
              result.Enqueue(GetToken(characters, tokenStopCharacters));
            }
            break;
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
        Evaluate(condition);
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