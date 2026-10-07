using System.Linq.Expressions;
using System.Reflection;
using Merp.Application.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Merp.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Rewrites <see cref="CustomFieldFiltering.Contain"/> to Npgsql's <c>EF.Functions.JsonContains</c> (jsonb
/// <c>@&gt;</c>) before the query is translated (D30).
/// </summary>
internal sealed class CustomFieldQueryInterceptor : IQueryExpressionInterceptor
{
    public static readonly CustomFieldQueryInterceptor Instance = new();

    private static readonly MethodInfo Contain = typeof(CustomFieldFiltering).GetMethod(nameof(CustomFieldFiltering.Contain))!;

    private static readonly MethodInfo JsonContains = typeof(NpgsqlJsonDbFunctionsExtensions).GetMethod(
        nameof(NpgsqlJsonDbFunctionsExtensions.JsonContains),
        [typeof(DbFunctions), typeof(object), typeof(object)])!;

    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData) =>
        new Rewriter().Visit(queryExpression);

    private sealed class Rewriter : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method != Contain)
            {
                return base.VisitMethodCall(node);
            }

            return Expression.Call(
                JsonContains,
                Expression.Constant(EF.Functions),
                Expression.Convert(Visit(node.Arguments[0]), typeof(object)),
                Expression.Convert(Visit(node.Arguments[1]), typeof(object)));
        }
    }
}
