using System;
using Xunit;

namespace GsPlugin.Tests {
    public class LinkingResultTests {
        [Fact]
        public void CreateSuccess_SetsSuccessTrue() {
            var result = LinkingResult.CreateSuccess("user-123", LinkingContext.ManualSettings);
            Assert.True(result.Success);
        }

        [Fact]
        public void CreateSuccess_SetsUserId() {
            var result = LinkingResult.CreateSuccess("user-123", LinkingContext.ManualSettings);
            Assert.Equal("user-123", result.UserId);
        }

        [Fact]
        public void CreateSuccess_SetsContext() {
            var result = LinkingResult.CreateSuccess("user-123", LinkingContext.AutomaticUri);
            Assert.Equal(LinkingContext.AutomaticUri, result.Context);
        }

        [Fact]
        public void CreateSuccess_ErrorMessageIsNull() {
            var result = LinkingResult.CreateSuccess("user-123", LinkingContext.ManualSettings);
            Assert.Null(result.ErrorMessage);
        }

        [Fact]
        public void CreateSuccess_ExceptionIsNull() {
            var result = LinkingResult.CreateSuccess("user-123", LinkingContext.ManualSettings);
            Assert.Null(result.Exception);
        }

        [Fact]
        public void CreateSuccess_NullUserId_IsAllowed() {
            var result = LinkingResult.CreateSuccess(null, LinkingContext.ManualSettings);
            Assert.True(result.Success);
            Assert.Null(result.UserId);
        }

        [Fact]
        public void CreateError_SetsSuccessFalse() {
            var result = LinkingResult.CreateError("Something failed", LinkingContext.ManualSettings);
            Assert.False(result.Success);
        }

        [Fact]
        public void CreateError_SetsErrorMessage() {
            var result = LinkingResult.CreateError("Token expired", LinkingContext.ManualSettings);
            Assert.Equal("Token expired", result.ErrorMessage);
        }

        [Fact]
        public void CreateError_SetsContext() {
            var result = LinkingResult.CreateError("fail", LinkingContext.AutomaticUri);
            Assert.Equal(LinkingContext.AutomaticUri, result.Context);
        }

        [Fact]
        public void CreateError_WithException_SetsException() {
            var ex = new InvalidOperationException("test");
            var result = LinkingResult.CreateError("fail", LinkingContext.ManualSettings, ex);
            Assert.Same(ex, result.Exception);
        }

        [Fact]
        public void CreateError_WithoutException_ExceptionIsNull() {
            var result = LinkingResult.CreateError("fail", LinkingContext.ManualSettings);
            Assert.Null(result.Exception);
        }

        [Fact]
        public void CreateError_UserIdIsNull() {
            var result = LinkingResult.CreateError("fail", LinkingContext.ManualSettings);
            Assert.Null(result.UserId);
        }

        [Fact]
        public void CreateError_WithIsNetworkError_SetsIsNetworkErrorTrue() {
            var result = LinkingResult.CreateError("Network timeout", LinkingContext.ManualSettings, isNetworkError: true);
            Assert.True(result.IsNetworkError);
        }

        [Fact]
        public void CreateError_Default_IsNetworkErrorIsFalse() {
            var result = LinkingResult.CreateError("Token expired", LinkingContext.ManualSettings);
            Assert.False(result.IsNetworkError);
        }

        [Fact]
        public void CreateSuccess_IsNetworkErrorIsFalse() {
            var result = LinkingResult.CreateSuccess("user-123", LinkingContext.ManualSettings);
            Assert.False(result.IsNetworkError);
        }
    }
}
