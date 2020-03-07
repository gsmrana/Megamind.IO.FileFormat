using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Megamind.IO.FileFormat
{
    public class ElfManager
    {
        #region Data

        readonly string _filename;
        readonly StringBuilder _response = new StringBuilder();

        #endregion

        #region Properties

        public string CmdlineToolPath { get; set; } = "";
        public string Objcopy { get; set; } = "arm-none-eabi-objcopy.exe";
        public string Objdump { get; set; } = "arm-none-eabi-objdump.exe";
        public string Readelf { get; set; } = "arm-none-eabi-readelf.exe";

        #endregion

        #region ctor

        public ElfManager(string filename, string toolpath = "")
        {
            _filename = filename;
            CmdlineToolPath = toolpath;
        }

        #endregion

        #region Public Methods

        public string GetHeaders()
        {
            _response.Clear();
            ExecuteCommandLine(Readelf, string.Format(" -h -l -S \"{0}\"", _filename));
            return _response.ToString();
        }

        public string GetAllInfo()
        {
            _response.Clear();
            ExecuteCommandLine(Readelf, string.Format(" -all \"{0}\"", _filename));
            return _response.ToString();
        }

        public string ObjDump()
        {
            _response.Clear();
            ExecuteCommandLine(Objdump, string.Format(" \"{0}\"", _filename));
            return _response.ToString();
        }

        public string ObjCopy()
        {
            _response.Clear();
            ExecuteCommandLine(Objcopy, string.Format(" \"{0}\"", _filename));
            return _response.ToString();
        }

        #endregion

        #region Private Methods

        private void ExecuteCommandLine(string toolname, string args, int timeout = 5)
        {
            var toolfullpath = Path.Combine(CmdlineToolPath, toolname);
            if (!File.Exists(toolfullpath))
            {
                throw new Exception(toolname + " - cmdline tool not found!");
            }

            var startInfo = new ProcessStartInfo(toolfullpath)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = args
            };

            var process = new Process();
            process.StartInfo = startInfo;
            process.ErrorDataReceived += Process_ErrorDataReceived;
            process.OutputDataReceived += Process_OutputDataReceived;
            process.Start();
            process.BeginErrorReadLine();
            process.BeginOutputReadLine();

            var sw = new Stopwatch();
            sw.Start();
            while (!process.HasExited)
            {
                Thread.Sleep(500);
                if (sw.Elapsed.Seconds > timeout)
                {
                    process.Kill();
                    throw new Exception("CLI Execution Timeout!");
                }
            }
        }

        private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            _response.Append(e.Data);
            _response.Append("\r");
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            _response.Append(e.Data);
            _response.Append("\r");
        }

        #endregion
    }
}
