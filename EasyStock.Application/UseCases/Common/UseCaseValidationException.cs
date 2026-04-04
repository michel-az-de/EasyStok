using System;

namespace EasyStock.Application.UseCases.Common
{
    public class UseCaseValidationException(string message) : InvalidOperationException(message)
    {
    }
}
