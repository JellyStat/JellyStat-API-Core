using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellystat_API_Core.Filters
{
    /// <summary>
    /// Action filter that validates the presence of a specific header and extracts its value.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class ValidateHeaderAttribute : Attribute, IAsyncActionFilter
    {
        private readonly string _headerName;
        private readonly bool _isRequired;

        /// <summary>
        /// Initializes a new instance of the ValidateHeaderAttribute.
        /// </summary>
        /// <param name="headerName">The name of the header to validate.</param>
        /// <param name="isRequired">Whether the header is required. Default is true.</param>
        public ValidateHeaderAttribute(string headerName, bool isRequired = true)
        {
            _headerName = headerName ?? throw new ArgumentNullException(nameof(headerName));
            _isRequired = isRequired;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (!context.HttpContext.Request.Headers.TryGetValue(_headerName, out var headerValue))
            {
                if (_isRequired)
                {
                    context.Result = new BadRequestObjectResult(new
                    {
                        error = $"Missing required header: {_headerName}"
                    });
                    return;
                }
            }
            else
            {
                // Store the header value in HttpContext.Items for later use
                context.HttpContext.Items[$"Header_{_headerName}"] = headerValue.ToString();
            }

            await next();
        }
    }
}
