﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    // Gets fully configured WorkerConfigs from IWorkerProviders
    internal class WorkerConfigFactory
    {
        private readonly IConfiguration _config;
        private readonly ILogger _logger;
        private Dictionary<string, IWorkerProvider> _workerProviderDictionary = new Dictionary<string, IWorkerProvider>();

        public WorkerConfigFactory(IConfiguration config, ILogger logger)
        {
            _config = config;
            _logger = logger;
            WorkersDirPath = Path.Combine(Path.GetDirectoryName(new Uri(typeof(WorkerConfigFactory).Assembly.CodeBase).LocalPath), LanguageWorkerConstants.DefaultWorkersDirectoryName);
            var workersDirectorySection = _config.GetSection($"{LanguageWorkerConstants.LanguageWorkersSectionName}:{LanguageWorkerConstants.WorkersDirectorySectionName}");
            if (!string.IsNullOrEmpty(workersDirectorySection.Value))
            {
                WorkersDirPath = workersDirectorySection.Value;
            }
        }

        public string WorkersDirPath { get; }

        public IEnumerable<WorkerConfig> GetConfigs(IEnumerable<IWorkerProvider> providers)
        {
            foreach (var provider in providers)
            {
                var description = provider.GetDescription();
                _logger.LogTrace($"Worker path for language worker {description.Language}: {description.WorkerDirectory}");

                var arguments = new WorkerProcessArguments()
                {
                    ExecutablePath = description.DefaultExecutablePath,
                    WorkerPath = description.GetWorkerPath()
                };

                if (description.Language.Equals(LanguageWorkerConstants.JavaLanguageWorkerName))
                {
                    arguments.ExecutablePath = GetExecutablePathForJava();
                }

                if (provider.TryConfigureArguments(arguments, _config, _logger))
                {
                    yield return new WorkerConfig()
                    {
                        Description = description,
                        Arguments = arguments
                    };
                }
                else
                {
                    _logger.LogError($"Could not configure language worker {description.Language}.");
                }
            }
        }

        public List<IWorkerProvider> GetWorkerProviders(ILogger logger, ScriptSettingsManager settingsManager = null, string language = null)
        {
            AddProviders(logger, language);
            AddProvidersFromAppSettings(logger);
            return _workerProviderDictionary.Values.ToList();
        }

        internal void AddProviders(ILogger logger, string language = null)
        {
            var providers = new List<IWorkerProvider>();
            logger.LogTrace($"Workers Directory set to: {WorkersDirPath}");

            if (!string.IsNullOrEmpty(language))
            {
                logger.LogInformation($"Reading Worker config for the language: {language}");
                AddProvider(Path.Combine(WorkersDirPath, language), logger);
            }
            else
            {
                logger.LogTrace($"Loading worker providers from the workers directory: {WorkersDirPath}");
                foreach (var workerDir in Directory.EnumerateDirectories(WorkersDirPath))
                {
                    string workerConfigPath = Path.Combine(workerDir, LanguageWorkerConstants.WorkerConfigFileName);
                    if (File.Exists(workerConfigPath))
                    {
                        AddProvider(workerDir, logger);
                    }
                }
            }
        }

        internal void AddProvidersFromAppSettings(ILogger logger)
        {
            var languagesSection = _config.GetSection($"{LanguageWorkerConstants.LanguageWorkersSectionName}");
            foreach (var languageSection in languagesSection.GetChildren())
            {
                var workerDirectorySection = languageSection.GetSection(LanguageWorkerConstants.WorkerDirectorySectionName);
                if (workerDirectorySection.Value != null)
                {
                    _workerProviderDictionary.Remove(languageSection.Key);
                    AddProvider(Path.Combine(workerDirectorySection.Value, languageSection.Key), logger);
                }
            }
        }

        internal void AddProvider(string workerDir, ILogger logger)
        {
            try
            {
                string workerConfigPath = Path.Combine(workerDir, LanguageWorkerConstants.WorkerConfigFileName);
                if (!File.Exists(workerConfigPath))
                {
                    logger.LogTrace($"Did not find worker config file at: {workerConfigPath}");
                    return;
                }
                logger.LogTrace($"Found worker config: {workerConfigPath}");
                string json = File.ReadAllText(workerConfigPath);
                JObject workerConfig = JObject.Parse(json);
                WorkerDescription workerDescription = workerConfig.Property(LanguageWorkerConstants.WorkerDescription).Value.ToObject<WorkerDescription>();
                workerDescription.WorkerDirectory = workerDir;
                var languageSection = _config.GetSection($"{LanguageWorkerConstants.LanguageWorkersSectionName}:{workerDescription.Language}");
                workerDescription.Arguments = workerDescription.Arguments ?? new List<string>();
                var argumentsSection = languageSection.GetSection($"{LanguageWorkerConstants.WorkerDescriptionArguments}");
                if (argumentsSection.Value != null)
                {
                    workerDescription.Arguments.AddRange(Regex.Split(argumentsSection.Value, @"\s+"));
                }
                if (File.Exists(workerDescription.GetWorkerPath()))
                {
                    logger.LogTrace($"Will load worker provider for language: {workerDescription.Language}");
                    _workerProviderDictionary[workerDescription.Language] = new GenericWorkerProvider(workerDescription, workerDir);
                }
                else
                {
                    throw new FileNotFoundException($"Did not find worker for for language: {workerDescription.Language}", workerDescription.GetWorkerPath());
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, $"Failed to initialize worker provider for: {workerDir}");
            }
        }

        internal string GetExecutablePathForJava()
        {
                string javaHome = ScriptSettingsManager.Instance.GetSetting("JAVA_HOME");
                if (string.IsNullOrEmpty(javaHome))
                {
                    return LanguageWorkerConstants.JavaLanguageWorkerName;
                }
                else
                {
                    if (ScriptSettingsManager.Instance.IsAppServiceEnvironment)
                    {
                        return Path.GetFullPath(Path.Combine(javaHome, "..", LanguageWorkerConstants.AppServiceEnvJavaVersion, "bin", LanguageWorkerConstants.JavaLanguageWorkerName));
                    }
                    else
                    {
                        return Path.GetFullPath(Path.Combine(javaHome, "bin", LanguageWorkerConstants.JavaLanguageWorkerName));
                    }
                }
        }
    }
}
