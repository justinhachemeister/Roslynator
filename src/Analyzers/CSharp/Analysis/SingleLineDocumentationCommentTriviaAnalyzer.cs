﻿// Copyright (c) Josef Pihrt. All rights reserved. Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Roslynator.CSharp.Syntax;
using static Roslynator.DiagnosticHelpers;

namespace Roslynator.CSharp.Analysis
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class SingleLineDocumentationCommentTriviaAnalyzer : BaseDiagnosticAnalyzer
    {
        private static readonly ImmutableArray<DiagnosticDescriptor> _supportedDiagnosticsWithoutFadeOut = ImmutableArray.Create(
            DiagnosticDescriptors.AddSummaryToDocumentationComment,
            DiagnosticDescriptors.AddSummaryElementToDocumentationComment,
            DiagnosticDescriptors.AddParamElementToDocumentationComment,
            DiagnosticDescriptors.AddTypeParamElementToDocumentationComment,
            DiagnosticDescriptors.UnusedElementInDocumentationComment,
            DiagnosticDescriptors.OrderElementsInDocumentationComment);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get { return _supportedDiagnosticsWithoutFadeOut.Add(DiagnosticDescriptors.UnusedElementInDocumentationCommentFadeOut); }
        }

        public override void Initialize(AnalysisContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));

            base.Initialize(context);
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(startContext =>
            {
                if (!startContext.AreAnalyzersSuppressed(_supportedDiagnosticsWithoutFadeOut))
                {
                    startContext.RegisterSyntaxNodeAction(AnalyzeSingleLineDocumentationCommentTrivia, SyntaxKind.SingleLineDocumentationCommentTrivia);
                }
            });
        }

        private static void AnalyzeSingleLineDocumentationCommentTrivia(SyntaxNodeAnalysisContext context)
        {
            var documentationComment = (DocumentationCommentTriviaSyntax)context.Node;

            if (!documentationComment.IsPartOfMemberDeclaration())
                return;

            bool containsInheritDoc = false;
            bool containsIncludeOrExclude = false;
            bool containsSummaryElement = false;
            bool containsContentElement = false;
            bool isFirst = true;

            CancellationToken cancellationToken = context.CancellationToken;

            SyntaxList<XmlNodeSyntax> content = documentationComment.Content;

            for (int i = 0; i < content.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                XmlElementInfo info = SyntaxInfo.XmlElementInfo(content[i]);

                if (info.Success)
                {
                    switch (info.GetElementKind())
                    {
                        case XmlElementKind.Include:
                        case XmlElementKind.Exclude:
                            {
                                if (isFirst)
                                    containsIncludeOrExclude = true;

                                break;
                            }
                        case XmlElementKind.InheritDoc:
                            {
                                containsInheritDoc = true;
                                break;
                            }
                        case XmlElementKind.Content:
                            {
                                containsContentElement = true;
                                break;
                            }
                        case XmlElementKind.Summary:
                            {
                                if (info.IsContentEmptyOrWhitespace)
                                    ReportDiagnosticIfNotSuppressed(context, DiagnosticDescriptors.AddSummaryToDocumentationComment, info.Element);

                                containsSummaryElement = true;
                                break;
                            }
                        case XmlElementKind.Code:
                        case XmlElementKind.Example:
                        case XmlElementKind.Remarks:
                        case XmlElementKind.Returns:
                        case XmlElementKind.Value:
                            {
                                if (info.IsContentEmptyOrWhitespace)
                                    ReportUnusedElement(context, info.Element, i, content);

                                break;
                            }
                    }

                    if (isFirst)
                    {
                        isFirst = false;
                    }
                    else
                    {
                        containsIncludeOrExclude = false;
                    }
                }
            }

            if (containsInheritDoc
                || containsIncludeOrExclude)
            {
                return;
            }

            if (!containsSummaryElement
                && !containsContentElement)
            {
                ReportDiagnosticIfNotSuppressed(context, DiagnosticDescriptors.AddSummaryElementToDocumentationComment, documentationComment);
            }

            SyntaxNode parent = documentationComment.ParentTrivia.Token.Parent;

            bool unusedElement = !context.IsAnalyzerSuppressed(DiagnosticDescriptors.UnusedElementInDocumentationComment);
            bool orderParams = !context.IsAnalyzerSuppressed(DiagnosticDescriptors.OrderElementsInDocumentationComment);
            bool addParam = !context.IsAnalyzerSuppressed(DiagnosticDescriptors.AddParamElementToDocumentationComment);
            bool addTypeParam = !context.IsAnalyzerSuppressed(DiagnosticDescriptors.AddTypeParamElementToDocumentationComment);

            if (addParam
                || orderParams
                || unusedElement)
            {
                SeparatedSyntaxList<ParameterSyntax> parameters = ParameterListInfo.Create(parent).Parameters;

                if (addParam
                    && parameters.Any())
                {
                    foreach (ParameterSyntax parameter in parameters)
                    {
                        if (IsMissing(documentationComment, parameter))
                        {
                            ReportDiagnostic(context, DiagnosticDescriptors.AddParamElementToDocumentationComment, documentationComment);
                            break;
                        }
                    }
                }

                if (orderParams || unusedElement)
                {
                    Analyze(context, documentationComment.Content, parameters, XmlElementKind.Param, (nodes, name) => nodes.IndexOf(name));
                }
            }

            if (addTypeParam
                || orderParams
                || unusedElement)
            {
                SeparatedSyntaxList<TypeParameterSyntax> typeParameters = TypeParameterListInfo.Create(parent).Parameters;

                if (addTypeParam
                    && typeParameters.Any())
                {
                    foreach (TypeParameterSyntax typeParameter in typeParameters)
                    {
                        if (IsMissing(documentationComment, typeParameter))
                        {
                            ReportDiagnostic(context, DiagnosticDescriptors.AddTypeParamElementToDocumentationComment, documentationComment);
                            break;
                        }
                    }
                }

                if (orderParams || unusedElement)
                {
                    Analyze(context, documentationComment.Content, typeParameters, XmlElementKind.TypeParam, (nodes, name) => nodes.IndexOf(name));
                }
            }
        }

        private static bool IsMissing(DocumentationCommentTriviaSyntax documentationComment, ParameterSyntax parameter)
        {
            foreach (XmlNodeSyntax xmlNode in documentationComment.Content)
            {
                XmlElementInfo elementInfo = SyntaxInfo.XmlElementInfo(xmlNode);

                if (elementInfo.Success
                    && !elementInfo.IsEmptyElement
                    && elementInfo.IsElementKind(XmlElementKind.Param))
                {
                    var element = (XmlElementSyntax)elementInfo.Element;

                    string value = element.GetAttributeValue("name");

                    if (value != null
                        && string.Equals(parameter.Identifier.ValueText, value, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool IsMissing(DocumentationCommentTriviaSyntax documentationComment, TypeParameterSyntax typeParameter)
        {
            foreach (XmlNodeSyntax xmlNode in documentationComment.Content)
            {
                XmlElementInfo elementInfo = SyntaxInfo.XmlElementInfo(xmlNode);

                if (elementInfo.Success
                    && !elementInfo.IsEmptyElement
                    && elementInfo.IsElementKind(XmlElementKind.TypeParam))
                {
                    var element = (XmlElementSyntax)elementInfo.Element;

                    string value = element.GetAttributeValue("name");

                    if (value != null
                        && string.Equals(typeParameter.Identifier.ValueText, value, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static void Analyze<TNode>(
            SyntaxNodeAnalysisContext context,
            SyntaxList<XmlNodeSyntax> xmlNodes,
            SeparatedSyntaxList<TNode> nodes,
            XmlElementKind kind,
            Func<SeparatedSyntaxList<TNode>, string, int> indexOf) where TNode : SyntaxNode
        {
            XmlNodeSyntax firstElement = null;

            int firstIndex = -1;

            for (int i = 0; i < xmlNodes.Count; i++)
            {
                XmlElementInfo elementInfo = SyntaxInfo.XmlElementInfo(xmlNodes[i]);

                if (!elementInfo.Success)
                    continue;

                if (!elementInfo.IsElementKind(kind))
                {
                    firstIndex = -1;
                    continue;
                }

                XmlNodeSyntax element = elementInfo.Element;

                string name = (element.IsKind(SyntaxKind.XmlElement))
                    ? ((XmlElementSyntax)element).GetAttributeValue("name")
                    : ((XmlEmptyElementSyntax)element).GetAttributeValue("name");

                if (name == null)
                {
                    firstIndex = -1;
                    continue;
                }

                int index = indexOf(nodes, name);

                if (index == -1)
                {
                    ReportUnusedElement(context, element, i, xmlNodes);
                }
                else if (index < firstIndex)
                {
                    ReportDiagnosticIfNotSuppressed(context, DiagnosticDescriptors.OrderElementsInDocumentationComment, firstElement);
                    return;
                }
                else
                {
                    firstElement = element;
                }

                firstIndex = index;
            }
        }

        private static void ReportUnusedElement(
            SyntaxNodeAnalysisContext context,
            XmlNodeSyntax xmlNode,
            int index,
            SyntaxList<XmlNodeSyntax> xmlNodes)
        {
            if (context.IsAnalyzerSuppressed(DiagnosticDescriptors.UnusedElementInDocumentationComment))
                return;

            ReportDiagnostic(context, DiagnosticDescriptors.UnusedElementInDocumentationComment, xmlNode);

            if (index > 0
                && xmlNodes[index - 1] is XmlTextSyntax xmlText)
            {
                SyntaxTokenList tokens = xmlText.TextTokens;

                if (tokens.Count == 1)
                {
                    if (tokens[0].IsKind(SyntaxKind.XmlTextLiteralToken))
                    {
                        SyntaxTrivia trivia = tokens[0].LeadingTrivia.SingleOrDefault(shouldThrow: false);

                        if (trivia.IsKind(SyntaxKind.DocumentationCommentExteriorTrivia))
                            ReportDiagnostic(context, DiagnosticDescriptors.UnusedElementInDocumentationCommentFadeOut, trivia);
                    }
                }
                else if (tokens.Count == 2)
                {
                    if (tokens[0].IsKind(SyntaxKind.XmlTextLiteralNewLineToken)
                        && tokens[1].IsKind(SyntaxKind.XmlTextLiteralToken))
                    {
                        SyntaxTrivia trivia = tokens[1].LeadingTrivia.SingleOrDefault(shouldThrow: false);

                        if (trivia.IsKind(SyntaxKind.DocumentationCommentExteriorTrivia))
                            ReportDiagnostic(context, DiagnosticDescriptors.UnusedElementInDocumentationCommentFadeOut, trivia);
                    }
                }
            }
        }
    }
}
