// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.ApiDescription.Tool.Commands
{
    internal class GetDocumentCommandWorker
    {
        public static int Process(GetDocumentCommandContext context)
        {
            var assemblyName = new AssemblyName(context.AssemblyName);
            var assembly = Assembly.Load(assemblyName);
            var entryPointType = assembly.EntryPoint?.DeclaringType;
            if (entryPointType == null)
            {
                Reporter.WriteError(Resources.FormatMissingEntryPoint(context.AssemblyPath));
                return 2;
            }

            var services = GetServices(entryPointType, context.AssemblyPath, context.AssemblyName);
            if (services == null)
            {
                return 3;
            }

            var success = TryProcess(context, services);
            if (!success)
            {
                // As part of the aspnet/Mvc#8425 fix, return 4 here.
                return 0;
            }

            return 0;
        }

        public static bool TryProcess(GetDocumentCommandContext context, IServiceProvider services)
        {
            var documentName = string.IsNullOrEmpty(context.DocumentName) ?
                GetDocumentCommand.FallbackDocumentName :
                context.DocumentName;
            var methodName = string.IsNullOrEmpty(context.Method) ?
                GetDocumentCommand.FallbackMethod :
                context.Method;
            var serviceName = string.IsNullOrEmpty(context.Service) ?
                GetDocumentCommand.FallbackService :
                context.Service;

            Reporter.WriteInformation(Resources.FormatUsingDocument(documentName));
            Reporter.WriteInformation(Resources.FormatUsingService(serviceName));

            string alternateMethodName = null;
            if (methodName.EndsWith("Async", StringComparison.Ordinal))
            {
                Reporter.WriteInformation(Resources.FormatUsingMethod(methodName));
            }
            else
            {
                alternateMethodName = methodName + "Async";
                Reporter.WriteInformation(Resources.FormatUsingMethods(methodName, alternateMethodName));
            }

            try
            {
                Type serviceType = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    serviceType = assembly.GetType(serviceName, throwOnError: false);
                    if (serviceType != null)
                    {
                        break;
                    }
                }

                if (serviceType == null)
                {
                    // As part of the aspnet/Mvc#8425 fix, make this an error unless the file already exists.
                    Reporter.WriteWarning(Resources.FormatServiceTypeNotFound(serviceName));
                    return true;
                }

                var method = serviceType.GetMethod(methodName, new[] { typeof(TextWriter), typeof(string) });
                if (method == null && alternateMethodName != null)
                {
                    method = serviceType.GetMethod(alternateMethodName, new[] { typeof(TextWriter), typeof(string) });
                }

                if (method == null)
                {
                    // As part of the aspnet/Mvc#8425 fix, make this an error unless the file already exists.
                    if (alternateMethodName == null)
                    {
                        Reporter.WriteWarning(Resources.FormatMethodNotFound(methodName, serviceName));
                    }
                    else
                    {
                        Reporter.WriteWarning(
                            Resources.FormatMethodsNotFound(methodName, alternateMethodName, serviceName));
                    }

                    return true;
                }

                var service = services.GetRequiredService(serviceType);
                if (service == null)
                {
                    // As part of the aspnet/Mvc#8425 fix, make this an error unless the file already exists.
                    Reporter.WriteWarning(Resources.FormatServiceNotFound(serviceName));
                    return true;
                }

                var success = true;
                using (var writer = File.CreateText(context.Output))
                {
                    var result = method.Invoke(service, new object[] { writer, documentName });
                    switch (result)
                    {
                        case null:
                            break;

                        case bool boolResult:
                            success = boolResult;
                            break;

                        case Task<bool> taskBoolResult:
                            taskBoolResult.Wait(TimeSpan.FromSeconds(30));
                            success = taskBoolResult.Result;
                            break;

                        case Task taskResult:
                            taskResult.Wait(TimeSpan.FromSeconds(30));
                            break;

                    }
                }

                if (!success)
                {
                    // As part of the aspnet/Mvc#8425 fix, make this an error unless the file already exists.
                    var message = Resources.FormatMethodInvocationFailed(methodName, serviceName, documentName);
                    Reporter.WriteWarning(message);
                }

                return success;
            }
            catch (Exception ex)
            {
                var message = FormatException(ex);

                // As part of the aspnet/Mvc#8425 fix, make this an error unless the file already exists.
                Reporter.WriteWarning(message);

                return false;
            }
        }

        // TODO: Use Microsoft.AspNetCore.Hosting.WebHostBuilderFactory.Sources once we have dev feed available.
        private static IServiceProvider GetServices(Type entryPointType, string assemblyPath, string assemblyName)
        {
            var args = new[] { Array.Empty<string>() };
            var methodInfo = entryPointType.GetMethod("BuildWebHost");
            if (methodInfo != null)
            {
                // BuildWebHost (old style has highest priority)
                var parameters = methodInfo.GetParameters();
                if (!methodInfo.IsStatic ||
                    parameters.Length != 1 ||
                    typeof(string[]) != parameters[0].ParameterType ||
                    typeof(IWebHost) != methodInfo.ReturnType)
                {
                    Reporter.WriteError(
                        "BuildWebHost method found in {assemblyPath} does not have expected signature.");

                    return null;
                }

                try
                {
                    var webHost = (IWebHost)methodInfo.Invoke(obj: null, parameters: args);

                    return webHost.Services;
                }
                catch (Exception ex)
                {
                    Reporter.WriteError($"BuildWebHost method threw: {FormatException(ex)}");

                    return null;
                }
            }

            if ((methodInfo = entryPointType.GetMethod("CreateWebHostBuilder")) != null)
            {
                // CreateWebHostBuilder
                var parameters = methodInfo.GetParameters();
                if (!methodInfo.IsStatic ||
                    parameters.Length != 1 ||
                    typeof(string[]) != parameters[0].ParameterType ||
                    typeof(IWebHostBuilder) != methodInfo.ReturnType)
                {
                    Reporter.WriteError(
                        "CreateWebHostBuilder method found in {assemblyPath} does not have expected signature.");

                    return null;
                }

                try
                {
                    var builder = (IWebHostBuilder)methodInfo.Invoke(obj: null, parameters: args);

                    return builder.Build().Services;
                }
                catch (Exception ex)
                {
                    Reporter.WriteError($"CreateWebHostBuilder method threw: {FormatException(ex)}");

                    return null;
                }
            }

            return null;
        }

        private static string FormatException(Exception exception)
        {
            return $"{exception.GetType().FullName}: {exception.Message}";
        }
    }
}
