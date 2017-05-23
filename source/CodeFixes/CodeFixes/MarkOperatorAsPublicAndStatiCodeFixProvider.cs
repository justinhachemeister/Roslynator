﻿// Copyright (c) Josef Pihrt. All rights reserved. Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Roslynator.CSharp.Comparers;

namespace Roslynator.CSharp.CodeFixes
{
    [ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MarkOperatorAsPublicAndStatiCodeFixProvider))]
    [Shared]
    public class MarkOperatorAsPublicAndStatiCodeFixProvider : BaseCodeFixProvider
    {
        public sealed override ImmutableArray<string> FixableDiagnosticIds
        {
            get { return ImmutableArray.Create(CSharpErrorCodes.UserDefinedOperatorMustBeDeclaredStaticAndPublic); }
        }

        public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
        {
            if (!Settings.IsCodeFixEnabled(CodeFixIdentifiers.MarkOperatorAsPublicAndStatic))
                return;

            SyntaxNode root = await context.GetSyntaxRootAsync().ConfigureAwait(false);

            MemberDeclarationSyntax memberDeclaration = root
                .FindNode(context.Span, getInnermostNodeForTie: true)?
                .FirstAncestorOrSelf<MemberDeclarationSyntax>();

            Debug.Assert(memberDeclaration != null, $"{nameof(memberDeclaration)} is null");

            if (memberDeclaration == null)
                return;

            foreach (Diagnostic diagnostic in context.Diagnostics)
            {
                switch (diagnostic.Id)
                {
                    case CSharpErrorCodes.UserDefinedOperatorMustBeDeclaredStaticAndPublic:
                        {
                            SyntaxTokenList modifiers = memberDeclaration.GetModifiers();

                            string title = "Add ";

                            if (modifiers.Contains(SyntaxKind.PublicKeyword))
                            {
                                title += "'static' modifier";
                            }
                            else if (modifiers.Contains(SyntaxKind.StaticKeyword))
                            {
                                title += "'public' modifier";
                            }
                            else
                            {
                                title += "'public static' modifiers";
                            }

                            CodeAction codeAction = CodeAction.Create(
                                title,
                                cancellationToken =>
                                {
                                    SyntaxTokenList newModifiers = modifiers;

                                    if (!modifiers.Contains(SyntaxKind.PublicKeyword))
                                        newModifiers = newModifiers.InsertModifier(SyntaxKind.PublicKeyword, ModifierComparer.Instance);

                                    if (!modifiers.Contains(SyntaxKind.StaticKeyword))
                                        newModifiers = newModifiers.InsertModifier(SyntaxKind.StaticKeyword, ModifierComparer.Instance);

                                    MemberDeclarationSyntax newMemberDeclaration = memberDeclaration.WithModifiers(newModifiers);

                                    return context.Document.ReplaceNodeAsync(memberDeclaration, newMemberDeclaration, cancellationToken);
                                },
                                diagnostic.Id + EquivalenceKeySuffix);

                            context.RegisterCodeFix(codeAction, diagnostic);
                            break;
                        }
                }
            }
        }
    }
}
