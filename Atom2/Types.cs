using System.Collections.Generic;

namespace Atom2
{
  public class Characters : System.Collections.Generic.Queue<char> { public Characters(char[] characters) : base(characters) { } }
  public class CharHashSet : System.Collections.Generic.HashSet<char> { }
  public class Items : System.Collections.Generic.List<object> { public Items() { } public Items(IEnumerable<object> collection) : base(collection) { } }
  public class NameHashSet : System.Collections.Generic.HashSet<Name> { }
  public class Stack : System.Collections.Generic.Stack<object> { }
  public class Tokens : System.Collections.Generic.Queue<object> { }
  public class Words : ScopedDictionary<Name, object> { }
}
