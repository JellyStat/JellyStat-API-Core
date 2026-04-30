using Gridify;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellystat_API_Core.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class DefaultGridifyOrderAttribute : ActionFilterAttribute
    {
        private readonly string _orderBy;

        public DefaultGridifyOrderAttribute(string propertyName, bool descending = false)
        {
            _orderBy = descending ? $"{propertyName} desc" : propertyName;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            foreach (var argument in context.ActionArguments.Values)
            {
                if (argument is GridifyQuery query && string.IsNullOrWhiteSpace(query.OrderBy))
                {
                    query.OrderBy = _orderBy;
                }
            }

            base.OnActionExecuting(context);
        }
    }
}
