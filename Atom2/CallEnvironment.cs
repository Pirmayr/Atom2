namespace Atom2
{
  public sealed class CallEnvironment
  {
    public object CurrentItem { get; set; }
    public Items Items { get; set; }
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public ScopedDictionary<Name, object>.Scope Scope { get; set; }
  }
}