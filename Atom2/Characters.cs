using System.Collections.Generic;

namespace Atom2
{
  public sealed class Characters : Queue<char>
  {
    public Characters(IEnumerable<char> characters) : base(characters) { }
  }
}