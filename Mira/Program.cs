namespace Atom2
{
  using System;
  using Eto.Forms;

  public static class Program
  {
    [STAThread]
    public static void Main(params string[] arguments)
    {
      try
      {
        string baseDirectory = "/Users/pic/Projects/Mira/Mira/System";
        string codeFilename = "Program.txt";
        if (arguments.Length == 2)
        {
          baseDirectory = arguments[0];
          codeFilename = arguments[1];
        }
        Application application = new Application();
        application.Run(new Editor(application, baseDirectory, codeFilename));
      }
      catch (Exception exception)
      {
        Console.Write(exception.Message);
      }
    }
  }
}