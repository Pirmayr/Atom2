namespace Atom2
{
  using System.Collections.Generic;

  public sealed class Characters : Queue<char>
  {
    public Characters(IEnumerable<char> characters) : base(characters)
    {
    }
  }
}