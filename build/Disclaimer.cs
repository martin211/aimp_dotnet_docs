// Copyright Matthias Koch 2018.
// Distributed under the MIT License.
// https://github.com/nuke-build/web/blob/master/LICENSE

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;

static class Disclaimer
{
    public static void WriteDisclaimer(ApiProject apiProject, string disclaimerFile, IEnumerable<string> solutionFiles)
    {
        var msBuildWorkspace = MSBuildWorkspace.Create(
            new Dictionary<string, string>
            {
                { "Configuration", "Release" },
                { "TargetFramework", "net461" }
            });

        var solutions = solutionFiles.Select(x => msBuildWorkspace.OpenSolutionAsync(x).Result).ToList();
        var relevantSymbols = (
                from solution in solutions
                from project in solution.Projects
                let compilation = project.GetCompilationAsync().Result
                from document in project.Documents
                let syntaxTree = document.GetSyntaxTreeAsync().Result
                let semanticModel = compilation.GetSemanticModel(syntaxTree)
                from declarationSyntax in syntaxTree.GetCompilationUnitRoot().DescendantNodes().OfType<MemberDeclarationSyntax>()
                where declarationSyntax is BaseTypeDeclarationSyntax || declarationSyntax is DelegateDeclarationSyntax
                select semanticModel.GetDeclaredSymbol(declarationSyntax).ToDisplayString())
            .Distinct().ToList();

        File.WriteAllText(disclaimerFile,
            relevantSymbols.Aggregate(new StringBuilder(),
                    (sb, x) => sb
                        .AppendLine()
                        .AppendLine("---")
                        .AppendLine($"uid: {x}")
                        .AppendLine("---")
                        .AppendLine()
                        .WriteWarning(apiProject)
                        .WriteInformation(apiProject)
                        .AppendLine())
                .ToString());
    }

    static StringBuilder WriteWarning(this StringBuilder builder, ApiProject apiProject)
    {
        const string org = "nuke-build";

        var owner = apiProject.Repository.Identifier;
        if (owner == org)
            return builder;

        return builder
            .AppendLine("<div class=\"alert alert-warning\" role=\"warning\">")
            .AppendLine("  <span class=\"icon icon-warning alert-icon\"></span>")
            .AppendLine($"  Solely maintained by <a href=\"https://github.com/{owner}\"><strong>@{owner}</strong></a>")
            .AppendLine($"  Listing approved by the <a href=\"https://github.com/{org}\"><strong>@{org}</strong></a> organization.")
            .AppendLine("</div>");
    }

    static StringBuilder WriteInformation(this StringBuilder builder, ApiProject apiProject)
    {
        var packageId = apiProject.PackageId;
        var repository = apiProject.Repository;
        return builder
            .AppendLine("<div class=\"alert alert-info\" role=\"info\">")
            .AppendLine("  <span class=\"icon icon-info alert-icon\"></span>")
            .AppendLine($"  This API is part of the <a href=\"https://nuget.org/packages/{packageId}\"><strong>{packageId}</strong></a> package.")
            .AppendLine(
                $"  The code is available at <a href=\"{repository.HttpsUrl}\"><strong>{repository.Endpoint}/{repository.Identifier}</strong></a>.")
            .AppendLine("</div>");
    }
}
