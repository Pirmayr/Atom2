namespace Atom2
{
  public static class Extensions
  {
    public static string ToInformation(this object value)
    {
      return value == null ? "(null)" : value + " [" + value.GetType().Name + "]";
    }

    public static Items ToItems(this object value)
    {
      return value is Items items ? items : new Items(value);
    }
  }
}