﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Hl7.FhirPath;
using Hl7.FhirPath.Expressions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public class SearchParameterSupportResolver : ISearchParameterSupportResolver
    {
        private readonly ISearchParameterDefinitionManager _definitionManager;
        private readonly IFhirElementToSearchValueTypeConverterManager _searchValueTypeConverterManager;
        private static readonly FhirPathCompiler _compiler = new FhirPathCompiler();

        public SearchParameterSupportResolver(
            ISearchParameterDefinitionManager definitionManager,
            IFhirElementToSearchValueTypeConverterManager searchValueTypeConverterManager)
        {
            EnsureArg.IsNotNull(definitionManager, nameof(definitionManager));
            EnsureArg.IsNotNull(searchValueTypeConverterManager, nameof(searchValueTypeConverterManager));

            _definitionManager = definitionManager;
            _searchValueTypeConverterManager = searchValueTypeConverterManager;
        }

        public bool IsSearchParameterSupported(SearchParameterInfo parameterInfo)
        {
            EnsureArg.IsNotNull(parameterInfo, nameof(parameterInfo));

            Expression parsed = _compiler.Parse(parameterInfo.Expression);

            (SearchParamType Type, Expression, Uri DefinitionUrl)[] componentExpressions = parameterInfo.Component
                .Select(x => (_definitionManager.GetSearchParameter(x.DefinitionUrl).Type,
                    _compiler.Parse(x.Expression),
                    x.DefinitionUrl))
                .ToArray();

            if (!parameterInfo.TargetResourceTypes.Any() && !parameterInfo.BaseResourceTypes.Any())
            {
                throw new NotSupportedException("No target resources defined.");
            }

            foreach (var resource in parameterInfo.TargetResourceTypes.Concat(parameterInfo.BaseResourceTypes))
            {
                SearchParameterTypeResult[] results = SearchParameterToTypeResolver.Resolve(
                    resource,
                    (parameterInfo.Type, parsed, parameterInfo.Url),
                    componentExpressions).ToArray();

                var converters = results
                    .Select(result => (
                        result,
                        hasConverter: _searchValueTypeConverterManager.TryGetConverter(
                            result.ClassMapping.NativeType,
                            SearchIndexer.GetSearchValueTypeForSearchParamType(result.SearchParamType),
                            out IFhirElementToSearchValueTypeConverter converter),
                        converter))
                    .ToArray();

                if (!converters.All(x => x.hasConverter))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
