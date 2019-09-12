using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Atom2
{
    using Tokens = Queue<string>;
    using CharHashSet = HashSet<char>;

    public class System
    {
        private const char Eof = Char.MinValue;
        private const char Whitespace = Char.MaxValue;

        private class Items : List<object> { }
        private class Words : Dictionary<string, object> { }
        private class Stack : Stack<object> { }

        private readonly Words words = new Words();
        private readonly Stack stack = new Stack();
        private readonly CharHashSet stringStopCharacters = new CharHashSet { Eof, '\'' };
        private readonly CharHashSet tokenStopCharacters = new CharHashSet { Eof, Whitespace, '(', ')', '\'' };

        public System()
        {
            words.Add("new", new Action(New));
            words.Add("invoke", new Action(Invoke));
            words.Add("not-equal", new Action(NotEqual));
            words.Add("less-or-equal", new Action(LessOrEqual));
            words.Add("add", new Action(Add));
            words.Add("subtract", new Action(Subtract));
            words.Add("set", new Action(Set));
            words.Add("get", new Action(Get));
            words.Add("if", new Action(If));
            words.Add("while", new Action(While));
            words.Add("show", new Action(Show));
        }

        public void Run(string code)
        {
            Evaluate(GetItems(GetTokens(code)));
        }

        private object Invoke(string assemblyName, string typeName, string memberName, BindingFlags memberKind, BindingFlags memberType, params object[] arguments)
        {
            Assembly assembly = Assembly.LoadWithPartialName(assemblyName);
            Type type = assembly.GetType(typeName);
            BindingFlags bindingFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | memberKind | memberType;
            bool isInstance = memberType.HasFlag(BindingFlags.Instance);
            bool isConstructor = memberKind.HasFlag(BindingFlags.CreateInstance);
            object target = isInstance && !isConstructor ? stack.Pop() : null;
            object result = type.InvokeMember(memberName, bindingFlags, null, target, arguments);
            return result;
        }

        private void Add()
        {
            dynamic b = stack.Pop();
            dynamic a = stack.Pop();
            stack.Push(a + b);
        }

        private void AddOrSetWord(string key, object word)
        {
            if (words.ContainsKey(key))
            {
                words[key] = word;
            }
            else
            {
                words.Add(key, word);
            }
        }

        private void Evaluate(object instance)
        {
            if (instance is Items list)
            {
                foreach (var currentItem in list)
                {
                    Process(currentItem);
                }
            }
            else
            {
                Process(instance);
            }
        }

        private void Get()
        {
            foreach (var currentItem in (Items) stack.Pop())
            {
                stack.Push(words[currentItem.ToString()]);
            }
        }

        private Items GetItems(Tokens tokens)
        {
            var result = new Items();
            while (0 < tokens.Count)
            {
                var currentToken = tokens.Dequeue();
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

        private string GetToken(Queue<char> characters, HashSet<char> stopCharacters)
        {
            var result = "";
            while (!stopCharacters.Contains(NextCharacter(characters)))
            {
                result += characters.Dequeue();
            }
            return result;
        }

        private Tokens GetTokens(string code)
        {
            var result = new Tokens();
            var characters = new Queue<char>(code.ToCharArray());
            for (var nextCharacter = NextCharacter(characters); nextCharacter != Eof; nextCharacter = NextCharacter(characters))
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
            var condition = stack.Pop();
            var body = stack.Pop();
            Evaluate(condition);
            if ((dynamic) stack.Pop() != 0)
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
            int parametersCount = (int) stack.Pop();
            var arguments = new object[parametersCount];
            for (var i = parametersCount - 1; 0 <= i; --i)
            {
                arguments[i] = stack.Pop();
            }
            stack.Push(Invoke(assemblyName, typeName, memberName, memberKind, memberType, arguments));
        }

        private void LessOrEqual()
        {
            dynamic b = stack.Pop();
            dynamic a = stack.Pop();
            stack.Push(a <= b ? 1 : 0);
        }

        private void New()
        {
            dynamic typeName = stack.Pop();
            dynamic assemblyName = stack.Pop();
            dynamic parametersCount = stack.Pop();
            var parameters = new object[parametersCount];
            for (var i = parametersCount - 1; 0 <= i; --i)
            {
                parameters[i] = stack.Pop();
            }
            Assembly assembly = Assembly.LoadWithPartialName(assemblyName);
            Type type = assembly.GetType(typeName);
            stack.Push(Activator.CreateInstance(type, parameters));
        }

        private char NextCharacter(Queue<char> characters)
        {
            var result = (0 < characters.Count) ? characters.Peek() : Char.MinValue;
            return result == Eof ? result : Char.IsWhiteSpace(result) ? Whitespace : result;
        }

        private void NotEqual()
        {
            dynamic b = stack.Pop();
            dynamic a = stack.Pop();
            stack.Push(a != b ? 1 : 0);
        }

        private void Process(object instance)
        {
            if (words.TryGetValue(instance.ToString(), out object word))
            {
                if (word is Action action)
                {
                    action.Invoke();
                }
                else
                {
                    Evaluate(word);
                }
            }
            else
            {
                stack.Push(instance);
            }
        }

        private void Set()
        {
            foreach (var currentItem in Enumerable.Reverse((Items) stack.Pop()))
            {
                AddOrSetWord(currentItem.ToString(), stack.Pop());
            }
        }

        private void Show()
        {
            Console.WriteLine(stack.Pop());
        }

        private void Subtract()
        {
            dynamic b = stack.Pop();
            dynamic a = stack.Pop();
            stack.Push(a - b);
        }

        private object ToObject(string token)
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

        private void While()
        {
            var condition = stack.Pop();
            var body = stack.Pop();
            Evaluate(condition);
            while ((dynamic) stack.Pop() != 0)
            {
                Evaluate(body);
                Evaluate(condition);
            }
        }
    }
}
