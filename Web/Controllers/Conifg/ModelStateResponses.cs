using Microsoft.AspNetCore.Mvc;
using Web.Extensions;
using Web.Resources;

namespace Web.Controllers.Conifg
{
    public static class ModelStateResponses
    {
        public static IActionResult ProduceErrorResponse(ActionContext context)
        {
            var error = context.ModelState.GetErrorMessage();
            var response = new ErrorResource(message: error);

            return new BadRequestObjectResult(response);
        }
    }
}
