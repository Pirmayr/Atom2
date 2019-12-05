namespace Mira
{
  using System;
  using System.Collections.Generic;

  public delegate void OutputtingEventHandler(object sender, string message);

  public delegate void TerminatingEventHandler(object sender, Exception exception);

  public sealed class CharHashSet : HashSet<char>
  {
  }

  public sealed class StringHashSet : HashSet<string>
  {
  }

  public sealed class NameHashSet : HashSet<Name>
  {
  }

  public sealed class Stack : Stack<object>
  {
    public IEnumerable<object> Pop(int count)
    {
      object[] result = new object[count];
      for (int i = count - 1; 0 <= i; --i)
      {
        result[i] = Pop();
      }
      return result;
    }
  }

  public sealed class Tokens : Queue<object>
  {
  }

  public sealed class Words : ScopedDictionary<Name, object>
  {
  }

  public sealed class CallEnvironments : Stack<CallEnvironment>
  {
  }
}