namespace Atom2
{
  public sealed class Name
  {
    public string Value { get; set; }

    public override bool Equals(object obj)
    {
      return obj is Name name && Value == name.Value;
    }

    public override int GetHashCode()
    {
      return (Value != null ? Value.GetHashCode() : 0);
    }

    public override string ToString()
    {
      return Value.ToString();
    }
  }
}