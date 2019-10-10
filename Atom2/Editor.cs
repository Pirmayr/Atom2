using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Mime;
using System.Windows.Forms;

namespace Atom2.System
{
  public class Editor : Form
  {
    private readonly TextBox editWindow = new TextBox();
    private readonly Runtime runtime;

    [STAThread]
    public static void Main(params string[] arguments)
    {
      try
      {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new Editor(arguments[0]));
      }
      catch (Exception exception)
      {
        Console.Write(exception.Message);
      }
    }

    public Editor(string baseDirectory)
    {
      runtime = new Runtime(baseDirectory);

      editWindow.Multiline = true;
      editWindow.Dock = DockStyle.Fill;

      ListBox stackWindow = new ListBox
      {
        Dock = DockStyle.Left,
        IntegralHeight = false
      };

      foreach (FontFamily oneFontFamily in FontFamily.Families)
      {
        stackWindow.Items.Add(oneFontFamily.Name);
      }

      ListBox trackWindow = new ListBox
      {
        Dock = DockStyle.Right,
        IntegralHeight = false
      };

      RichTextBox outputWindow = new RichTextBox
      {
        Dock = DockStyle.Bottom
      };

      MenuStrip menu = new MenuStrip();
      menu.Items.Add("Open").Click += OnOpen;
      menu.Items.Add("Run").Click += OnRun;

      Controls.Add(editWindow);
      Controls.Add(outputWindow);
      Controls.Add(stackWindow);
      Controls.Add(trackWindow);
      Controls.Add(menu);

      WindowState = FormWindowState.Maximized;
    }

    private void OnRun(object sender, EventArgs e)
    {
      runtime.Run(editWindow.Text);
    }

    private void OnOpen(object sender, EventArgs e)
    {
      OpenFileDialog dialog = new OpenFileDialog();
      if (dialog.ShowDialog() == DialogResult.OK)
      {
        editWindow.Text = File.ReadAllText(dialog.FileName);
      }
    }
  }
}
