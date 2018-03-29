using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Nuke.Common;
using Nuke.Common.Tools.MSBuild;
using Nuke.Core;
using Nuke.Core.BuildServers;
using Nuke.Core.Execution;
using Nuke.Core.IO;
using Nuke.Core.Utilities.Collections;
using static Nuke.Core.Logger;

static class CustomToc
{
    private class RelevantType
    {
        public string Area { get; private set; }

        public INamedTypeSymbol TypeSymbol { get; private set; }

        public RelevantType(string area, INamedTypeSymbol typeSymbol)
        {
            Area = area;
            TypeSymbol = typeSymbol;
        }
    }


    public static void WriteCustomToc(string tocFile, IEnumerable<string> solutionFiles)
    {
        var msBuildWorkspace = MSBuildWorkspace.Create(
            new Dictionary<string, string>
            {
                { "Configuration", "Release" },
                { "TargetFramework", "net461" }
            });
        msBuildWorkspace.WorkspaceFailed += (s, e) =>
        {
            if (e.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure)
                Warn(e.Diagnostic.Message);
            else
                Warn(e.Diagnostic.Message);
        };

        var solutions = solutionFiles.Select(x => LoadSolution(msBuildWorkspace, x)).ToList();

        var iconClasses = (
                from solution in solutions
                from project in solution.Projects.Where(c => c.Name.Equals("AIMP.SDK"))
                let compilation = project.GetCompilationAsync().Result
                from document in project.Documents
                let syntaxTree = document.GetSyntaxTreeAsync().Result
                let semanticModel = compilation.GetSemanticModel(syntaxTree)
                from attributeSyntax in syntaxTree.GetCompilationUnitRoot().DescendantNodes().OfType<AttributeSyntax>()
                let attributeSymbol = semanticModel.GetSymbolInfo(attributeSyntax.Name).Symbol
                where typeof(IconClassAttribute).Name.Equals(attributeSymbol?.ContainingType.Name)
                let arguments = attributeSyntax.ArgumentList.Arguments
                let typeOfExpression = (TypeOfExpressionSyntax) arguments.First().Expression
                select new
                       {
                           ClassFullName = semanticModel.GetSymbolInfo(typeOfExpression.Type).Symbol.ToDisplayString(),
                           IconClass = (string) semanticModel.GetConstantValue(arguments.Last().Expression).Value
                       })
            .ToDictionary(x => x.ClassFullName, x => x.IconClass);

        var relevantTypeSymbols = (
                from solution in solutions
                from project in solution.Projects.Where(c => c.Name.Equals("AIMP.SDK"))
                let compilation = project.GetCompilationAsync().Result
                from document in project.Documents
                let syntaxTree = document.GetSyntaxTreeAsync().Result
                let semanticModel = compilation.GetSemanticModel(syntaxTree)
                from interfaceDeclarationSyntax in syntaxTree.GetCompilationUnitRoot().DescendantNodes().OfType<InterfaceDeclarationSyntax>()
                let typeSymbol = semanticModel.GetDeclaredSymbol(interfaceDeclarationSyntax)
                let kind = GetKind(typeSymbol, iconClasses)
                let area = GetArea(typeSymbol)
                where typeSymbol.ContainingAssembly.Name != ".build" && kind != Kind.None
                select new { TypeSymbol = typeSymbol, Kind = kind, Area = area })
            .Distinct(x => x.TypeSymbol.ToDisplayString())
            .ForEachLazy(x => Info($"Found '{x.TypeSymbol.ToDisplayString()}' ({x.Kind})."))
            .ToLookup(x => x.Kind, x => new RelevantType(x.Area, x.TypeSymbol));

        TextTasks.WriteAllText(tocFile,
            new StringBuilder()
                .WriteBlock(Kind.Objects, relevantTypeSymbols, iconClasses)
                .WriteBlock(Kind.Core, relevantTypeSymbols, iconClasses)
                .WriteBlock(Kind.Entry, relevantTypeSymbols, iconClasses)
                .WriteBlock(Kind.Servers, relevantTypeSymbols, iconClasses)
                .WriteBlock(Kind.Common, relevantTypeSymbols, iconClasses)
                .WriteBlock(Kind.Addons, relevantTypeSymbols, iconClasses)
                .ToString());
    }

    static Solution LoadSolution(MSBuildWorkspace msBuildWorkspace, string solutionFile)
    {
        var solution = msBuildWorkspace.OpenSolutionAsync(solutionFile).Result;
        return solution;
    }

    enum Kind
    {
        None,
        Objects,
        Core,
        Entry,
        Servers,
        Common,
        Addons
    }

    private static Kind GetKind(ITypeSymbol typeSymbol, Dictionary<string, string> iconClasses)
    {
        if (IsEntryType(typeSymbol, iconClasses))
            return Kind.Entry;
        if (IsServerType(typeSymbol))
            return Kind.Servers;
        if (typeSymbol.Name.EndsWith("Tasks"))
            return IsCommonType(typeSymbol)
                ? Kind.Common
                : Kind.Addons;

        if (typeSymbol.ContainingNamespace.Name.Equals("SDK"))
        {
            return Kind.Core;
        }

        if (typeSymbol.ContainingNamespace.Name.Equals("Objects"))
        {
            return Kind.Objects;
        }

        return Kind.Common;
    }

    private static string GetArea(ITypeSymbol typeSymbol)
    {
        return typeSymbol.ContainingNamespace.Name.Equals("SDK")
            ? "Core"
            : typeSymbol.ContainingNamespace.Name;
    }

    static StringBuilder WriteBlock(this StringBuilder builder, Kind kind, ILookup<Kind, RelevantType> typeSymbols, IDictionary<string, string> iconClasses)
    {
        var typesByArea = typeSymbols[kind].GroupBy(c => c.Area, c => c.TypeSymbol);

        builder.AppendLine($"- separator: {kind}");

        foreach (IGrouping<string, INamedTypeSymbol> namedTypeSymbols in typesByArea)
        {
            builder.AppendLine($"- area: {namedTypeSymbols.Key}");
            builder.ForEach(namedTypeSymbols, c => builder.WriteType(c, iconClasses));
        }

        return builder;
    }

    static StringBuilder ForEach<T>(this StringBuilder builder, IEnumerable<T> enumerable, Action<T> builderAction)
    {
        foreach (var item in enumerable)
            builderAction(item);
        return builder;
    }

    static StringBuilder WriteType(this StringBuilder builder, ITypeSymbol typeSymbol, IDictionary<string, string> iconClasses)
        => builder
            .AppendLine($"- uid: {typeSymbol.ToDisplayString()}")
            .AppendLine($"  name: {typeSymbol.GetName()}")
            .AppendLine($"  icon: {typeSymbol.GetIconClassText(iconClasses)}");


    static bool IsEntryType(ITypeSymbol typeSymbol, Dictionary<string, string> iconClasses)
    {
        if (!iconClasses.ContainsKey(typeSymbol.ToDisplayString()))
            return false;

        if (typeSymbol.ContainingAssembly.Name == typeof(NukeBuild).Assembly.GetName().Name)
            return true;

        return new[]
               {
                   typeof(DefaultSettings)
               }.Any(x => typeSymbol.ToDisplayString().Equals(x.FullName));
    }

    static bool IsServerType(this ITypeSymbol typeSymbol)
        => typeSymbol.GetAttributes().Any(x => x.AttributeClass.ToDisplayString().Equals(typeof(BuildServerAttribute).FullName));

    static bool IsCommonType(ITypeSymbol typeSymbol)
        => typeSymbol.ContainingAssembly.Name == typeof(MSBuildTasks).Assembly.GetName().Name;

    static string GetName(this ITypeSymbol typeSymbol)
        => typeSymbol.Name.EndsWith("Tasks")
            ? typeSymbol.Name.Substring(startIndex: 0, length: typeSymbol.Name.Length - "Tasks".Length)
            : typeSymbol.Name;

    static string GetIconClassText(this ITypeSymbol typeSymbol, IDictionary<string, string> iconClasses)
    {
        if (iconClasses.TryGetValue(typeSymbol.ToDisplayString(), out var iconClass))
            return iconClass;
        if (IsServerType(typeSymbol))
            return "server";

        return "power-cord2";
    }
}
