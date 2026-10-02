// Copyright (c) Beztek Software Solutions. All rights reserved.

namespace Beztek.Facade.Sql
{
    using System;

    /// <summary>
    /// Short-lived password for <see cref="SqlFacadeConfig.PasswordProvider"/>.
    /// <see cref="ExpiresAt"/> is required: the provider path is for rotating
    /// credentials (cloud IAM tokens). Put static secrets in
    /// <see cref="SqlFacadeConfig.ConnectionString"/> and omit the provider.
    /// </summary>
    public readonly struct SqlPassword
    {
        public SqlPassword(string password, DateTimeOffset expiresAt)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password must not be null or blank.", nameof(password));

            Password = password;
            ExpiresAt = expiresAt;
        }

        public string Password { get; }

        /// <summary>
        /// UTC instant after which this password must not be reused.
        /// The facade remints at <c>ExpiresAt - PasswordRefreshSkew</c>.
        /// </summary>
        public DateTimeOffset ExpiresAt { get; }
    }
}
