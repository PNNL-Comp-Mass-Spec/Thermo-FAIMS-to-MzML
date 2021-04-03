using System;
using System.IO;
using PRISM;
using PRISM.FileProcessor;

namespace ThermoFAIMStoMzML
{
    internal static class Program
    {
        // Ignore Spelling: conf

        private const string PROGRAM_DATE = "2021-04-03";

        private static int Main(string[] args)
        {
            var exeName = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
            var exePath = ProcessFilesOrDirectoriesBase.GetAppPath();
            var cmdLineParser = new CommandLineParser<ThermoFAIMStoMzMLOptions>(exeName, GetAppVersion())
            {
                ProgramInfo = "This program converts a Thermo .raw file with FAIMS scans into a series of .mzML files, " +
                              "creating one .mzML file for each FAIMS compensation voltage (CV) value in the .raw file",
                ContactInfo = "Program written by Matthew Monroe for PNNL (Richland, WA) in 2020" + Environment.NewLine +
                              "E-mail: matthew.monroe@pnnl.gov or proteomics@pnnl.gov" + Environment.NewLine +
                              "Website: https://omics.pnl.gov/ or https://panomics.pnnl.gov/"
            };

            // Allow /Conf in addition to /ParamFile for specifying a text file with Key=Value options
            cmdLineParser.AddParamFileKey("Conf");

            cmdLineParser.UsageExamples.Add("Program syntax:" + Environment.NewLine + Path.GetFileName(exePath) + " " +
                                            "/I:InputFileNameOrDirectoryPath [/O:OutputDirectoryName] " + Environment.NewLine +
                                            "[/S] [/R:LevelsToRecurse] [/Preview] " + Environment.NewLine +
                                            "[/IE] [/L] [/LogFile:LogFileName]");

            var result = cmdLineParser.ParseArgs(args);
            var options = result.ParsedResults;
            if (!result.Success || !options.Validate())
            {
                // Delay for 750 msec in case the user double clicked this file from within Windows Explorer (or started the program via a shortcut)
                System.Threading.Thread.Sleep(750);
                return -1;
            }

            try
            {
                var processor = new ThermoFAIMStoMzMLProcessor(options);

                processor.DebugEvent += Processor_DebugEvent;
                processor.ErrorEvent += Processor_ErrorEvent;
                processor.WarningEvent += Processor_WarningEvent;
                processor.StatusEvent += Processor_MessageEvent;
                processor.ProgressUpdate += Processor_ProgressUpdate;

                bool success;

                if (options.RecurseDirectories)
                {
                    success = processor.ProcessFilesAndRecurseDirectories(
                        options.InputDataFilePath,
                        options.OutputDirectoryPath,
                        options.MaxLevelsToRecurse);
                }
                else
                {
                    success = processor.ProcessFilesWildcard(
                       options.InputDataFilePath,
                       options.OutputDirectoryPath);
                }

                if (success)
                    return 0;

                return -1;
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error occurred in modMain->Main", ex);
                return -1;
            }
        }

        private static string GetAppVersion()
        {
            return ProcessFilesOrDirectoriesBase.GetAppVersion(PROGRAM_DATE);
        }

        private static void ShowErrorMessage(string message, Exception ex = null)
        {
            ConsoleMsgUtils.ShowError(message, ex);
        }

        private static void Processor_DebugEvent(string message)
        {
            ConsoleMsgUtils.ShowDebug(message);
        }

        private static void Processor_ErrorEvent(string message, Exception ex)
        {
            ConsoleMsgUtils.ShowErrorCustom(message, ex, false);
        }

        private static void Processor_MessageEvent(string message)
        {
            Console.WriteLine(message);
        }

        private static void Processor_ProgressUpdate(string progressMessage, float percentComplete)
        {
            // Console.WriteLine();
            // Processor_DebugEvent(percentComplete.ToString("0.0") + "%, " + progressMessage);
        }

        private static void Processor_WarningEvent(string message)
        {
            ConsoleMsgUtils.ShowWarning(message);
        }
    }
}
