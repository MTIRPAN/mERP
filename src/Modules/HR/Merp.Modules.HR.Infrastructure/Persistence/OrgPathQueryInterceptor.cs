using System.Linq.Expressions;
using System.Reflection;
using Merp.Modules.HR.Domain.Organization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Merp.Modules.HR.Infrastructure.Persistence;

/// <summary>
/// Rewrites <see cref="OrgPaths.IsWithin"/> to Npgsql's <see cref="LTree.IsDescendantOf"/> (ltree <c>&lt;@</c>, served
/// by the GiST index of <c>path</c>) before the query is translated, so application code keeps the domain's method
/// (D37).
/// </summary>
internal sealed class OrgPathQueryInterceptor : IQueryExpressionInterceptor
{
    public static readonly OrgPathQueryInterceptor Instance = new();

    private static readonly MethodInfo IsWithin = typeof(OrgPaths).GetMethod(nameof(OrgPaths.IsWithin))!;

    private static readonly MethodInfo IsDescendantOf = typeof(LTree).GetMethod(nameof(LTree.IsDescendantOf), [typeof(LTree)])!;

    private static readonly MethodInfo ToLTree = typeof(LTree).GetMethod("op_Implicit", [typeof(string)])!;

    public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData) =>
        new Rewriter().Visit(queryExpression);

    private sealed class Rewriter : ExpressionVisitor
    {
        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method != IsWithin)
            {
                return base.VisitMethodCall(node);
            }

            return Expression.Call(
                Expression.Convert(Visit(node.Arguments[0]), typeof(LTree), ToLTree),
                IsDescendantOf,
                Expression.Convert(Visit(node.Arguments[1]), typeof(LTree), ToLTree));
        }
    }
}
