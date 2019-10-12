using System.Collections.Generic;

#pragma warning disable 618

namespace Atom2
{
  public class Scope<TK, TV> : Dictionary<TK, TV> { }

  public sealed class Scopes<TK, TV> : Stack<Scope<TK, TV>> { }
}