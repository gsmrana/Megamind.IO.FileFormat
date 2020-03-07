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

        readonly string _sourcefilename;
        readonly StringBuilder _response = new StringBuilder();

        #endregion

        #region Properties

        public static string CmdlineToolPath { get; set; } = "";
        public static int CmdlineExecTimeoutSec { get; set; } = 5;

        public static string Readelf { get; set; } = "arm-none-eabi-readelf.exe";
        public static string Objcopy { get; set; } = "arm-none-eabi-objcopy.exe";
        public static string Objdump { get; set; } = "arm-none-eabi-objdump.exe";
        public static string Sizetool { get; set; } = "arm-none-eabi-size.exe";

        public static readonly Dictionary<string, string> CmdLineTools = new Dictionary<string, string>()
        {
            { "readelf", Readelf },
            { "objcopy", Objcopy },
            { "objdump", Objdump },
            { "size", Sizetool },
        };

        #endregion

        #region ctor

        public ElfManager(string filename = "")
        {
            _sourcefilename = filename;
        }

        #endregion

        #region Public Methods

        public string GetAllHeadersInfo()
        {
            _response.Clear();
            ExecuteCommandLine(Readelf, string.Format(" -all \"{0}\"", _sourcefilename));
            return _response.ToString();
        }

        readonly Dictionary<string, string> ExtToFileType = new Dictionary<string, string>
        {
            { ".bin", "binary" },
            { ".hex", "ihex" },
            { ".srec", "srec" },
        };

        public string SaveOutputFile(string outputfilename)
        {
            var inext = Path.GetExtension(_sourcefilename).ToLower();
            var outext = Path.GetExtension(outputfilename).ToLower();          
            var outputtype = ExtToFileType[outext];
            var inputtype = "";
            if (ExtToFileType.ContainsKey(inext))
                inputtype = " -I " + ExtToFileType[inext];

            _response.Clear();
            ExecuteCommandLine(Objcopy, string.Format("{0} -O {1} \"{2}\" \"{3}\" -v", inputtype, outputtype, _sourcefilename, outputfilename));
            return _response.ToString();
        }

        public string GetDisassemblyText()
        {
            _response.Clear();
            ExecuteCommandLine(Objdump, string.Format(" -d -S \"{0}\"", _sourcefilename));
            return _response.ToString();
        }

        public string GetSizeInfo()
        {
            _response.Clear();
            ExecuteCommandLine(Sizetool, string.Format(" \"{0}\"", _sourcefilename));
            return _response.ToString();
        }

        public string ExecuteCommandline(string toolname, string cmdline)
        {
            if (CmdLineTools.ContainsKey(toolname))
                toolname = CmdLineTools[toolname];
            _response.Clear();
            ExecuteCommandLine(toolname, cmdline);
            return _response.ToString();
        }

        #endregion

        #region Private Methods

        private string FindToolFullname(string toolname)
        {
            var toolfullname = Path.Combine(CmdlineToolPath, toolname);
            if (!File.Exists(toolfullname))
            {
                if (File.Exists(toolname))
                    toolfullname = toolname;
                else throw new Exception(toolname + " - cmdline tool not found!");
            }                
            return toolfullname;
        }

        private void ExecuteCommandLine(string toolname, string args)
        {
            var toolfullname = FindToolFullname(toolname);
            var startInfo = new ProcessStartInfo(toolfullname)
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

            var starttime = DateTime.Now; 
            while (!process.HasExited)
            {
                Thread.Sleep(500);
                if (DateTime.Now.Subtract(starttime).TotalSeconds > CmdlineExecTimeoutSec)
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
