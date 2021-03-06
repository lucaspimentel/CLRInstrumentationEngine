﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace InstrEngineTests
{
    internal class ProfilerHelpers
    {
        #region private fields
        private static readonly Guid ProfilerGuid = new Guid("{324F817A-7420-4E6D-B3C1-143FBED6D855}");

        private const string HostConfig32PathEnvName = "MicrosoftInstrumentationEngine_ConfigPath32_";
        private const string HostConfig64PathEnvName = "MicrosoftInstrumentationEngine_ConfigPath64_";

        private const string TestOutputEnvName = "Nagler_TestOutputPath";
        private const string TestScriptFileEnvName = "Nagler_TestScript";
        private const string TestOutputFileEnvName = "MicrosoftInstrumentationEngine_FileLogPath";
        private const string IsRejitEnvName = "Nagler_IsRejit";

        #endregion

        // In order to debug the host process, set this to true and a messagebox will be thrown early in the profiler
        // startup to allow attaching a debugger.
        private static bool ThrowMessageBoxAtStartup = false;

        private static bool BinaryRecompiled = false;

        public static void LaunchAppAndCompareResult(string testApp, string fileName, string args = null)
        {
            // Usually we use the same file name for test script, baseline and test result
            ProfilerHelpers.LaunchAppUnderProfiler(testApp, fileName, fileName, false, args);
            ProfilerHelpers.DiffResultToBaseline(fileName, fileName);
        }

        public static void LaunchAppUnderProfiler(string testApp, string testScript, string output, bool isRejit, string args)
        {
            if (!BinaryRecompiled)
            {
                BinaryRecompiled = true;
                TargetAppCompiler.DeleteExistingBinary(PathUtils.GetAssetsPath());
                TargetAppCompiler.ComplileCSharpTestCode(PathUtils.GetAssetsPath());
            }

            DeleteOutputFileIfExist(output);

            // Ensure test results path exists
            Directory.CreateDirectory(PathUtils.GetTestResultsPath());

            bool is32bitTest = Is32bitTest(testScript);
            string bitnessSuffix = is32bitTest ? "x86" : "x64";

            // TODO: call this only for 64bit OS
            SetBitness(is32bitTest);

            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
            psi.UseShellExecute = false;
            psi.EnvironmentVariables.Add("COR_ENABLE_PROFILING", "1");
            psi.EnvironmentVariables.Add("COR_PROFILER", ProfilerGuid.ToString("B"));
            psi.EnvironmentVariables.Add("COR_PROFILER_PATH", Path.Combine(PathUtils.GetAssetsPath(), string.Format("MicrosoftInstrumentationEngine_{0}.dll", bitnessSuffix)));
            psi.EnvironmentVariables.Add("MicrosoftInstrumentationEngine_FileLog", "Dumps");

            // Uncomment this line to debug tests
            //psi.EnvironmentVariables.Add("MicrosoftInstrumentationEngine_DebugWait", "1");

            if (ThrowMessageBoxAtStartup)
            {
                psi.EnvironmentVariables.Add("MicrosoftInstrumentationEngine_MessageboxAtAttach", @"1");
            }

            if (TestParameters.DisableMethodSignatureValidation)
            {
                psi.EnvironmentVariables.Add("MicrosoftInstrumentationEngine_DisableCodeSignatureValidation", @"1");
            }

            psi.EnvironmentVariables.Add(TestOutputEnvName, PathUtils.GetAssetsPath());
            psi.EnvironmentVariables.Add(
                is32bitTest? HostConfig32PathEnvName : HostConfig64PathEnvName,
                Path.Combine(PathUtils.GetAssetsPath(), string.Format("NaglerInstrumentationMethod_{0}.xml", bitnessSuffix)));

            string scriptPath = Path.Combine(PathUtils.GetTestScriptsPath(), testScript);

            psi.EnvironmentVariables.Add(TestScriptFileEnvName, scriptPath);

            string outputPath = Path.Combine(PathUtils.GetTestResultsPath(), output);

            psi.EnvironmentVariables.Add(TestOutputFileEnvName, outputPath);
            psi.EnvironmentVariables.Add(IsRejitEnvName, isRejit ? "True" : "False");

            psi.FileName = Path.Combine(PathUtils.GetAssetsPath(), testApp);
            psi.Arguments = args;

            System.Diagnostics.Process testProcess = System.Diagnostics.Process.Start(psi);
            testProcess.WaitForExit();

            Assert.AreEqual(0, testProcess.ExitCode, "Test application failed");
        }

        public static string[] SplitXmlDocuments(string content)
        {
            List<string> docs = new List<string>();
            const string xmlDeclarationString = "<?xml version=\"1.0\"?>";
            int idx = 0;
            int iNext = content.IndexOf(xmlDeclarationString, idx + 1);

            while (iNext != -1)
            {
                string doc = content.Substring(idx, iNext - idx);
                docs.Add(doc);
                idx = iNext;
                iNext = content.IndexOf(xmlDeclarationString, idx + 1);
            }

            iNext = content.Length;
            string lastDoc = content.Substring(idx, iNext - idx);
            docs.Add(lastDoc);

            return docs.ToArray();
        }

        public static void DiffResultToBaseline(string output, string baseline)
        {
            string outputPath = Path.Combine(PathUtils.GetTestResultsPath(), output);
            string baselinePath = Path.Combine(PathUtils.GetBaselinesPath(), baseline);

            string baselineStr;
            StringBuilder outputStrBuilder = new StringBuilder();
            string outputStr;

            using (StreamReader baselineStream = new StreamReader(baselinePath))
            {
                using (StreamReader outputStream = new StreamReader(outputPath))
                {
                    baselineStr = baselineStream.ReadToEnd();
                    string tmpStr;
                    while ((tmpStr = outputStream.ReadLine()) != null)
                    {
                        if (!string.IsNullOrEmpty(tmpStr) &&
                            !tmpStr.StartsWith("[TestIgnore]"))
                        {
                            outputStrBuilder.Append(tmpStr);
                        }
                    }
                    outputStr = outputStrBuilder.ToString();
                }
            }

            string[] baselineDocs = SplitXmlDocuments(baselineStr);
            string[] outputDocs = SplitXmlDocuments(outputStr);

            Assert.AreEqual(baselineDocs.Length, outputDocs.Length);
            for (int docIdx = 0; docIdx < baselineDocs.Length; docIdx++)
            {

                string baselineXmlDocStr = baselineDocs[docIdx];
                string outputXmlDocStr = outputDocs[docIdx];

                XmlDocument baselineDocument = new XmlDocument();
                baselineDocument.LoadXml(baselineXmlDocStr);

                XmlDocument outputDocument = new XmlDocument();
                outputDocument.LoadXml(outputXmlDocStr);

                Assert.AreEqual(baselineDocument.ChildNodes.Count, outputDocument.ChildNodes.Count);

                for (int i = 0; i < baselineDocument.ChildNodes.Count; i++)
                {
                    XmlNode currBaselineNode = baselineDocument.ChildNodes[i];
                    XmlNode currOutputNode = outputDocument.ChildNodes[i];

                    DiffResultToBaselineNode(currBaselineNode, currOutputNode);
                }
            }
        }

        private static void DiffResultToBaselineNode(XmlNode baselineNode, XmlNode outputNode)
        {
            const string VolatileAttribute = "Volatile";

            if (String.CompareOrdinal(baselineNode.Name, outputNode.Name) != 0)
            {
                Assert.Fail("Baseline node name does not equal output node name\n" + baselineNode.Name + "\n" + outputNode.Name);
                return;
            }

            bool isVolatile = baselineNode.Attributes != null &&
                baselineNode.Attributes[VolatileAttribute] != null &&
                string.Equals(baselineNode.Attributes[VolatileAttribute].Value, "True", StringComparison.OrdinalIgnoreCase);

            // Don't check values of nodes marked Volatile.
            // NOTE: Eventually this should also be made to do regexp matching against
            if (!isVolatile)
            {
                if (CompareOrdinalNormalizeLineEndings(baselineNode.Value, outputNode.Value) != 0)
                {
                    Assert.Fail("Baseline value does not equal output value\n" + baselineNode.Value + "\n" + outputNode.Value);
                    return;
                }

                Assert.AreEqual(baselineNode.ChildNodes.Count, outputNode.ChildNodes.Count);

                for (int i = 0; i < baselineNode.ChildNodes.Count; i++)
                {
                    XmlNode currBaselineNode = baselineNode.ChildNodes[i];
                    XmlNode currOutputNode = outputNode.ChildNodes[i];

                    DiffResultToBaselineNode(currBaselineNode, currOutputNode);
                }
            }
        }

        // Do this because we are doing git autocrlf stuff so the baselines will have ???
        // but the test output files will always have Windows-style.
        private static int CompareOrdinalNormalizeLineEndings(string a, string b)
        {
            string normalA = a?.Replace("\r\n", "\n");
            string normalB = a?.Replace("\r\n", "\n");
            return String.CompareOrdinal(normalA, normalB);
        }


        private static XmlDocument LoadTestScript(string testScript)
        {
            string scriptPath = Path.Combine(PathUtils.GetTestScriptsPath(), testScript);

            XmlDocument scriptDocument = new XmlDocument();
            scriptDocument.Load(scriptPath);

            return scriptDocument;
        }

        private static bool Is32bitTest(string testScript)
        {
            const string ScriptRootElementName = "InstrumentationTestScript";
            const string WowAttribute = "Use32BitProfiler";

            XmlDocument scriptDocument = LoadTestScript(testScript);

            foreach (XmlNode child in scriptDocument.ChildNodes)
            {
                if (string.Equals(child.Name, ScriptRootElementName, StringComparison.Ordinal))
                {
                    bool is32bitTest = child.Attributes != null &&
                        child.Attributes[WowAttribute] != null &&
                        string.Equals(child.Attributes[WowAttribute].Value, "True", StringComparison.OrdinalIgnoreCase);

                    return is32bitTest;
                }
            }

            // The default is 64 bit test
            return false;
        }

        private static void DeleteOutputFileIfExist(string output)
        {
            string outputPath = Path.Combine(PathUtils.GetTestResultsPath(), output);

            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }

        /// <summary>
        /// Specify the preferred bitness for launched test application
        /// </summary>
        private static void SetBitness(bool isWow64)
        {
            const int COMPLUS_ENABLE_64BIT = 0x00000001;

            int flag = NativeMethods.GetComPlusPackageInstallStatus();

            if (isWow64)
            {
                flag &= ~COMPLUS_ENABLE_64BIT;
            }
            else
            {
                flag |= COMPLUS_ENABLE_64BIT;
            }

            NativeMethods.SetComPlusPackageInstallStatus(flag);
        }
    }
}
