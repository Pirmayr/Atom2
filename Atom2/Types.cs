using System;
using System.Collections.Generic;

namespace Atom2
{
  public sealed class Characters : Queue<char>
  {
    public Characters(IEnumerable<char> characters) : base(characters) { }
  }

  public sealed class CharHashSet : HashSet<char> { }

  public sealed class StringHashSet : HashSet<string> { }

  public sealed class Items : List<object>
  {
    public Items() { }
    public Items(object instance) { Add(instance); }
    public Items(IEnumerable<object> collection) : base(collection) { }

    public override string ToString()
    {
      return "(" + string.Join(" ", ToArray()) + ")";
    }

    public static Items operator +(Items a, object b)
    {
      a.Add(b);
      return a;
    }

    public static Items operator +(object a, Items b)
    {
      b.Insert(0, a);
      return b;
    }
  }

  public sealed class NameHashSet : HashSet<Name> { }

  public sealed class Stack : Stack<object> { }

  public sealed class Tokens : Queue<object> { }

  public sealed class Words : ScopedDictionary<Name, object> { }

  public sealed class CallEnvironments : Stack<CallEnvironment> { }

  public delegate void OutputtingEventHandler(object sender, string message);
  public delegate void TerminatingEventHandler(object sender, Exception exception);
}