using System.Net;

namespace GsPlugin {
    /// <summary>
    /// Wraps an API response with success/failure status and error details.
    /// Provides richer error information than returning null on failure.
    /// </summary>
    /// <typeparam name="T">The response body type.</typeparam>
    public class ApiResult<T> where T : class {
        public bool Success { get; }
        public T Data { get; }
        public HttpStatusCode? StatusCode { get; }
        public string ErrorMessage { get; }

        private ApiResult(bool success, T data, HttpStatusCode? statusCode, string errorMessage) {
            Success = success;
            Data = data;
            StatusCode = statusCode;
            ErrorMessage = errorMessage;
        }

        public static ApiResult<T> Ok(T data, HttpStatusCode statusCode = HttpStatusCode.OK) =>
            new ApiResult<T>(true, data, statusCode, null);

        public static ApiResult<T> Fail(string errorMessage, HttpStatusCode? statusCode = null) =>
            new ApiResult<T>(false, null, statusCode, errorMessage);
    }
}
