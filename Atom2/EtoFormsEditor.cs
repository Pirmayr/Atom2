using System;
using System.Collections.Generic;
using System.IO;
using Eto.Drawing;
using Eto.Forms;
using Items = System.Collections.Generic.List<object>;

namespace Atom2
{
  public sealed class EtoFormsEditor : Form
  {
    private static readonly Application Application = new Application();
    private static readonly Font StandardFont = new Font("Verdana", 10);
    private readonly TextArea codeTextArea;
    private readonly TextArea outputTextArea;
    private readonly TreeGridView codeTreeGridView;


    TreeGridView NewTreeGridView(params string[] headers)
    {
      TreeGridView result = new TreeGridView();
      GridColumnCollection gridColumns = new GridColumnCollection();
      for (int i = 0; i < headers.Length; ++i)
      {
        GridColumn currentGridColumn = new GridColumn();
        currentGridColumn.HeaderText = headers[i];
        currentGridColumn.Editable = true;
        currentGridColumn.DataCell = new TextBoxCell(i);
        result.Columns.Add(currentGridColumn);
      }

      /*
      TreeGridItemCollection treecollection = new TreeGridItemCollection();
      TreeGridItem item1 = new TreeGridItem { Values = new[] { "1", "2", "3" } };
      item1.Expanded = true;
      item1.Children.Add(new TreeGridItem { Values = new[] { "4", "5", "6" } });
      item1.Children.Add(new TreeGridItem { Values = new[] { "7", "8", "9" } });
      treecollection.Add(item1);
      TreeGridItem item2 = new TreeGridItem { Values = new[] { "a", "b", "c" } };
      treecollection.Add(item2);
      result.DataStore = treecollection;
      */

      var test = new GridColumn();



      result.Width = 300;

      return result;
    }


    private EtoFormsEditor(string startProgramPath)
    {
      Title = "Atom2";
      WindowState = WindowState.Maximized;

      Command openCommand = new Command(OnOpen);
      openCommand.MenuText = "&Open";
      Command runCommand = new Command(OnRun);
      runCommand.MenuText = "&Run";

      ButtonMenuItem fileMenuItem = new ButtonMenuItem();
      fileMenuItem.Text = "&File";
      fileMenuItem.Items.Add(openCommand);
      fileMenuItem.Items.Add(runCommand);

      MenuBar menuBar = new MenuBar();
      menuBar.Items.Add(fileMenuItem);
      Menu = menuBar;

      // Code:
      codeTextArea = new TextArea();
      codeTextArea.Font = StandardFont;
      codeTextArea.Text = Runtime.Code(startProgramPath);

      TableCell codeTableCell = new TableCell(codeTextArea, true);

      TableRow codeTableRow = new TableRow(codeTableCell);
      codeTableRow.ScaleHeight = true;

      // Output:
      outputTextArea = new TextArea();
      TableRow outputTableRow = new TableRow(outputTextArea);
      outputTextArea.Height = 300;

      // Central column:
      TableLayout centerTableLayout = new TableLayout(codeTableRow, outputTableRow);

      TableCell centerTableCell = new TableCell(centerTableLayout, true);

      // Left column:
      TreeGridView stackGridView = NewTreeGridView("Value", "Type");

      // Right column:
      codeTreeGridView = NewTreeGridView("Value", "Type");

      // Table row:
      TableRow tableRow = new TableRow(stackGridView, centerTableCell, codeTreeGridView);

      // Layout
      TableLayout layout = new TableLayout();
      layout.Rows.Add(tableRow);
      Content = layout;
    }

    private void OnOpen(object sender, EventArgs arguments)
    {
      MessageBox.Show("Open");
    }

    private void OnRun(object sender, EventArgs arguments)
    {
      if (!Program.Runtime.Run(codeTextArea.Text, out Exception exception))
      {
        outputTextArea.Text = exception.Message;
      }
      RebuildCodeWindow(codeTreeGridView);
    }

    private void RebuildCodeWindow(TreeGridView view)
    {
      TreeGridItemCollection collection = new TreeGridItemCollection();
      codeTreeGridView.DataStore = RebuildTrackWindow(Program.Runtime.CurrentRootItems);
    }

    private static TreeGridItemCollection RebuildTrackWindow(IEnumerable<object> rootItems, int indentation = 0)
    {
      TreeGridItemCollection result = new TreeGridItemCollection();
      string indentationPrefix = new string(' ', indentation * 2);
      foreach (object currentItem in rootItems)
      {
        if (currentItem is Items currentItems)
        {
          result.Add(new TreeGridItem(RebuildTrackWindow(currentItems, indentation + 1), "(Block)") { Expanded = true });
        }
        else
        {
          result.Add(new TreeGridItem(indentationPrefix + currentItem));
        }
      }
      return result;
    }

    public static void Run(string[] arguments)
    {
      Application.Run(new EtoFormsEditor(arguments[1]));
    }
  }
}