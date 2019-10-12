using System;
using System.Collections.Generic;
using Eto.Drawing;
using Eto.Forms;

namespace Atom2
{
  public sealed class Editor : Form
  {
    private static readonly Application Application = new Application();
    private static readonly Font StandardFont = new Font("Helvetica", 12);
    private readonly TextArea codeTextArea;
    private readonly TextArea outputTextArea;
    private readonly TreeGridView codeTreeGridView;
    private readonly TreeGridView stackGridView;


    TreeGridView NewTreeGridView(params string[] headers)
    {
      TreeGridView result = new TreeGridView();
      for (int i = 0; i < headers.Length; ++i)
      {
        GridColumn currentGridColumn = new GridColumn();
        currentGridColumn.HeaderText = headers[i];
        currentGridColumn.Editable = true;
        currentGridColumn.DataCell = new TextBoxCell(i);
        result.Columns.Add(currentGridColumn);
      }
      result.Width = 300;
      return result;
    }

    private Editor(string startProgramPath)
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
      outputTextArea.Height = 250;

      // Central column:
      TableLayout centerTableLayout = new TableLayout(codeTableRow, outputTableRow);

      TableCell centerTableCell = new TableCell(centerTableLayout, true);

      // Left column:
      stackGridView = NewTreeGridView("Value", "Type");

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
      RebuildCodeTreeView();
    }

    private void RebuildCodeTreeView()
    {
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
      Application.Run(new Editor(arguments[1]));
    }
  }
}