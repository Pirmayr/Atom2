using Eto;
using Platform = Eto.Mac.Platform;

namespace Mira
{
  using System;
  using System.Collections;
  using System.Windows.Forms;

  using Application = Eto.Forms.Application;

  public static class Program
  {
    [STAThread]
    public static void Main(params string[] arguments)
    {
      try
      {
        Console.WriteLine(Environment.CurrentDirectory);
        Console.WriteLine(Environment.CommandLine);

        Type platformType = Type.GetType(Platforms.Mac);
        Platform platform = (Platform) Activator.CreateInstance(platformType);
        Platform.Initialize(platform);
        
        string baseDirectory = "/Users/pic/Projects/Mira/Mira/System";
        string codeFilename = "Program.txt";
        if (arguments.Length == 2)
        {
          baseDirectory = arguments[0];
          codeFilename = arguments[1];
        }
        Application application = new Application(platform);
        application.Run(new Editor(application, baseDirectory, codeFilename));
      }
      catch (Exception exception)
      {
        Console.Write(exception.Message);
      }
    }
  }
}