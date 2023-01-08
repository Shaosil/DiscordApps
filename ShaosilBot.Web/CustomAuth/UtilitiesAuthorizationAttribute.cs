using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ShaosilBot.Web.CustomAuth
{
	public class UtilitiesAuthorizationAttribute : ActionFilterAttribute
	{
		private readonly IConfiguration _configuration;

		public UtilitiesAuthorizationAttribute(IConfiguration configuration)
		{
			_configuration = configuration;
		}

		public override void OnActionExecuting(ActionExecutingContext context)
		{
			context.HttpContext.Request.Headers.TryGetValue("UtilitiesAuthToken", out var token);

			if (string.IsNullOrWhiteSpace(token) || token != _configuration["UtilitiesAuthToken"])
				context.Result = new StatusCodeResult(StatusCodes.Status401Unauthorized);

			base.OnActionExecuting(context);
		}
	}
}