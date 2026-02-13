using System.Net;
using Xunit;

namespace GsPlugin.Tests {
    public class DummyResponse {
        public string Value { get; set; }
    }

    public class ApiResultTests {
        [Fact]
        public void Ok_SetsSuccessTrue() {
            var result = ApiResult<DummyResponse>.Ok(new DummyResponse { Value = "test" });
            Assert.True(result.Success);
        }

        [Fact]
        public void Ok_SetsData() {
            var data = new DummyResponse { Value = "hello" };
            var result = ApiResult<DummyResponse>.Ok(data);
            Assert.Same(data, result.Data);
        }

        [Fact]
        public void Ok_DefaultStatusCode_Is200() {
            var result = ApiResult<DummyResponse>.Ok(new DummyResponse());
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        }

        [Fact]
        public void Ok_CustomStatusCode() {
            var result = ApiResult<DummyResponse>.Ok(new DummyResponse(), HttpStatusCode.Created);
            Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        }

        [Fact]
        public void Ok_ErrorMessageIsNull() {
            var result = ApiResult<DummyResponse>.Ok(new DummyResponse());
            Assert.Null(result.ErrorMessage);
        }

        [Fact]
        public void Ok_NullData_IsAllowed() {
            var result = ApiResult<DummyResponse>.Ok(null);
            Assert.True(result.Success);
            Assert.Null(result.Data);
        }

        [Fact]
        public void Fail_SetsSuccessFalse() {
            var result = ApiResult<DummyResponse>.Fail("error");
            Assert.False(result.Success);
        }

        [Fact]
        public void Fail_SetsErrorMessage() {
            var result = ApiResult<DummyResponse>.Fail("something broke");
            Assert.Equal("something broke", result.ErrorMessage);
        }

        [Fact]
        public void Fail_DataIsNull() {
            var result = ApiResult<DummyResponse>.Fail("error");
            Assert.Null(result.Data);
        }

        [Fact]
        public void Fail_DefaultStatusCode_IsNull() {
            var result = ApiResult<DummyResponse>.Fail("error");
            Assert.Null(result.StatusCode);
        }

        [Fact]
        public void Fail_CustomStatusCode() {
            var result = ApiResult<DummyResponse>.Fail("not found", HttpStatusCode.NotFound);
            Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        }

        [Fact]
        public void Fail_ServerError_StatusCode() {
            var result = ApiResult<DummyResponse>.Fail("server error", HttpStatusCode.InternalServerError);
            Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
        }
    }
}
