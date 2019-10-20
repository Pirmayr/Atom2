using System.Collections.Generic;

namespace Atom2
{
  public sealed class Characters : Queue<char>
  {
    public Characters(IEnumerable<char> characters) : base(characters) { }
  }

  public sealed class CharHashSet : HashSet<char> { }

  public sealed class Items : List<object>
  {
    public Items() { }
    public Items(IEnumerable<object> collection) : base(collection) { }
  }

  public sealed class NameHashSet : HashSet<Name> { }

  public sealed class Stack : Stack<object> { }

  public sealed class Tokens : Queue<object> { }

  public sealed class Words : ScopedDictionary<Name, object> { }

  public sealed class CallEnvironments : Stack<CallEnvironment> { }
}