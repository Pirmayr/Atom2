#pragma warning disable 618

namespace Atom2
{
  public sealed class CallEnvironment
  {
    public object CurrentItem { get; set; }
    public Items Items { get; set; }
    public ScopedDictionary<Name, object>.Scope Scope { get; set; }
  }
}