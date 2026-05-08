using Gridify;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellystat_API_Core.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class DefaultGridifyPageSizeAttribute : ActionFilterAttribute
    {
        private readonly int _max;

        public DefaultGridifyPageSizeAttribute(int max)
        {
            _max = max;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            foreach (var argument in context.ActionArguments.Values)
            {
                if (argument is GridifyQuery query && query.PageSize != _max)
                {
                    query.PageSize = _max;
                }
            }

            base.OnActionExecuting(context);
        }
    }
}
