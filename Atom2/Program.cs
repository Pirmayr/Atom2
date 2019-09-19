using System;

namespace Atom2
{
  internal static class MainClass
  {
    public static void Main()
    {
      try
      {
        new Runtime().Run("Program.txt");
        Console.Write("done");
      }
      catch (Exception exception)
      {
        Console.Write(exception.Message);
      }
    }
  }
}