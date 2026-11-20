using System;
using System.Threading;
using System.Windows.Forms;

namespace GameInputLab
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
            {
                ShowFatalError(e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
            {
                Exception exception = e.ExceptionObject as Exception;
                if (exception != null)
                    ShowFatalError(exception);
            };

            Application.Run(new MainForm());
        }

        private static void ShowFatalError(Exception exception)
        {
            try
            {
                MessageBox.Show(
                    "程序遇到未处理错误。本次录制数据可能尚未保存。\n\n" + exception.Message,
                    "游戏输入实验室",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
            }
        }
    }
}
