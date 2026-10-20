export interface paths {
    "/api/v1/auth/accounts/{userId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        /** Deletes the account outright. */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    userId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Forbidden */
                403: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/accounts/{userId}/lockout": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Locks the account out indefinitely. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    userId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Forbidden */
                403: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        /** Lifts an administrative lockout. */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    userId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Forbidden */
                403: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/accounts/{userId}/roles/{role}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        /** Grants a role. */
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    role: string;
                    userId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Forbidden */
                403: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        post?: never;
        /** Revokes a role. */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    role: string;
                    userId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Forbidden */
                403: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/accounts/{userId}/security-events": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Lists the named account's security trail, newest first. */
        get: {
            parameters: {
                query?: {
                    Page?: number | string;
                    PageSize?: number | string;
                };
                header?: never;
                path: {
                    userId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PagedResponseOfSecurityEventResponse"];
                        "text/json": components["schemas"]["PagedResponseOfSecurityEventResponse"];
                        "text/plain": components["schemas"]["PagedResponseOfSecurityEventResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Forbidden */
                403: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/accounts/{userId}/two-factor": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        /**
         * Strips the account's second factor, for a holder who has lost both the authenticator and the
         *     recovery codes.
         */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    userId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Forbidden */
                403: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/change-email": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Starts a change of the caller's email address by mailing a single-use token to the new one. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["RequestEmailChangeRequest"];
                    "application/json": components["schemas"]["RequestEmailChangeRequest"];
                    "text/json": components["schemas"]["RequestEmailChangeRequest"];
                };
            };
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/change-password": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Replaces the caller's password. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["ChangePasswordRequest"];
                    "application/json": components["schemas"]["ChangePasswordRequest"];
                    "text/json": components["schemas"]["ChangePasswordRequest"];
                };
            };
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/confirm-email": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Confirms an email address. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["ConfirmEmailRequest"];
                    "application/json": components["schemas"]["ConfirmEmailRequest"];
                    "text/json": components["schemas"]["ConfirmEmailRequest"];
                };
            };
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/confirm-email-change": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Confirms a pending email change from the token mailed to the new address. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["ConfirmEmailChangeRequest"];
                    "application/json": components["schemas"]["ConfirmEmailChangeRequest"];
                    "text/json": components["schemas"]["ConfirmEmailChangeRequest"];
                };
            };
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/delete-account": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Deletes the caller's own account and ends every one of its sessions. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["DeleteMyAccountRequest"];
                    "application/json": components["schemas"]["DeleteMyAccountRequest"];
                    "text/json": components["schemas"]["DeleteMyAccountRequest"];
                };
            };
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Forbidden */
                403: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/forgot-password": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Starts a password reset by emailing a single-use token. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["RequestPasswordResetRequest"];
                    "application/json": components["schemas"]["RequestPasswordResetRequest"];
                    "text/json": components["schemas"]["RequestPasswordResetRequest"];
                };
            };
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/login": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Exchanges credentials for an access token and a refresh token. */
        post: {
            parameters: {
                query?: never;
                header?: {
                    /** @description Send 'cookie' to receive the refresh token in the 'refresh_token' cookie (HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth) instead of the body, which then omits refreshToken. */
                    "X-Refresh-Token-Mode"?: string;
                };
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["LoginRequest"];
                    "application/json": components["schemas"]["LoginRequest"];
                    "text/json": components["schemas"]["LoginRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        /** @description In cookie mode, sets or clears the 'refresh_token' cookie (HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth), expiring with the refresh token. */
                        "Set-Cookie"?: string;
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["LoginResponse"];
                        "text/json": components["schemas"]["LoginResponse"];
                        "text/plain": components["schemas"]["LoginResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/login/external": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Exchanges an `id_token` the client obtained from an identity provider for this API's own
         *     token pair, creating or linking a local account on the way.
         */
        post: {
            parameters: {
                query?: never;
                header?: {
                    /** @description Send 'cookie' to receive the refresh token in the 'refresh_token' cookie (HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth) instead of the body, which then omits refreshToken. */
                    "X-Refresh-Token-Mode"?: string;
                };
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["SignInWithExternalProviderRequest"];
                    "application/json": components["schemas"]["SignInWithExternalProviderRequest"];
                    "text/json": components["schemas"]["SignInWithExternalProviderRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        /** @description In cookie mode, sets or clears the 'refresh_token' cookie (HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth), expiring with the refresh token. */
                        "Set-Cookie"?: string;
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ExternalLoginResponse"];
                        "text/json": components["schemas"]["ExternalLoginResponse"];
                        "text/plain": components["schemas"]["ExternalLoginResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/login/two-factor": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Exchanges the challenge token of a `twoFactorRequired` login, plus a code, for a token
         *     pair.
         */
        post: {
            parameters: {
                query?: never;
                header?: {
                    /** @description Send 'cookie' to receive the refresh token in the 'refresh_token' cookie (HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth) instead of the body, which then omits refreshToken. */
                    "X-Refresh-Token-Mode"?: string;
                };
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["VerifyTwoFactorRequest"];
                    "application/json": components["schemas"]["VerifyTwoFactorRequest"];
                    "text/json": components["schemas"]["VerifyTwoFactorRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        /** @description In cookie mode, sets or clears the 'refresh_token' cookie (HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth), expiring with the refresh token. */
                        "Set-Cookie"?: string;
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["LoginResponse"];
                        "text/json": components["schemas"]["LoginResponse"];
                        "text/plain": components["schemas"]["LoginResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/logout": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Revokes a refresh token, ending the session it belongs to. */
        post: {
            parameters: {
                query?: never;
                header?: {
                    /** @description Send 'cookie' to present the refresh token in the 'refresh_token' cookie instead of the body; the cookie is read only when this header is sent, which is what refuses a request forged from another site. */
                    "X-Refresh-Token-Mode"?: string;
                };
                path?: never;
                cookie?: {
                    /** @description The refresh token, read only when X-Refresh-Token-Mode is 'cookie'. */
                    refresh_token?: string;
                };
            };
            requestBody?: {
                content: {
                    "application/*+json": null | components["schemas"]["LogoutRequest"];
                    "application/json": null | components["schemas"]["LogoutRequest"];
                    "text/json": null | components["schemas"]["LogoutRequest"];
                };
            };
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        /** @description In cookie mode, sets or clears the 'refresh_token' cookie (HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth), expiring with the refresh token. */
                        "Set-Cookie"?: string;
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/logout-all": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Revokes every refresh token grant belonging to the caller, ending all of their sessions. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/me": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** The authenticated caller's own profile. */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CurrentUserResponse"];
                        "text/json": components["schemas"]["CurrentUserResponse"];
                        "text/plain": components["schemas"]["CurrentUserResponse"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        /** The authenticated caller's own profile. */
        head: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CurrentUserResponse"];
                        "text/json": components["schemas"]["CurrentUserResponse"];
                        "text/plain": components["schemas"]["CurrentUserResponse"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/me/security-events": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Lists the caller's own security trail, newest first. */
        get: {
            parameters: {
                query?: {
                    Page?: number | string;
                    PageSize?: number | string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PagedResponseOfSecurityEventResponse"];
                        "text/json": components["schemas"]["PagedResponseOfSecurityEventResponse"];
                        "text/plain": components["schemas"]["PagedResponseOfSecurityEventResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/passkeys": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Lists the passkeys registered on the caller's account, newest first. */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PasskeysResponse"];
                        "text/json": components["schemas"]["PasskeysResponse"];
                        "text/plain": components["schemas"]["PasskeysResponse"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/passkeys/{credentialId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        /** Removes one of the caller's passkeys by the id the passkey list names. */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    credentialId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/passkeys/register": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Completes a passkey registration with the created credential, its state, an optional label
         *     and the current password.
         */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["CompletePasskeyRegistrationRequest"];
                    "application/json": components["schemas"]["CompletePasskeyRegistrationRequest"];
                    "text/json": components["schemas"]["CompletePasskeyRegistrationRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PasskeyResponse"];
                        "text/json": components["schemas"]["PasskeyResponse"];
                        "text/plain": components["schemas"]["PasskeyResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Forbidden */
                403: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/passkeys/register/options": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Starts a passkey registration: options for `navigator.credentials.create()` and the
         *     state to post back.
         */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PasskeyRegistrationOptionsResponse"];
                        "text/json": components["schemas"]["PasskeyRegistrationOptionsResponse"];
                        "text/plain": components["schemas"]["PasskeyRegistrationOptionsResponse"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/passkeys/sign-in": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Completes a passkey sign-in, exchanging the assertion for a token pair. */
        post: {
            parameters: {
                query?: never;
                header?: {
                    /** @description Send 'cookie' to receive the refresh token in the 'refresh_token' cookie (HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth) instead of the body, which then omits refreshToken. */
                    "X-Refresh-Token-Mode"?: string;
                };
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["PasskeySignInRequest"];
                    "application/json": components["schemas"]["PasskeySignInRequest"];
                    "text/json": components["schemas"]["PasskeySignInRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        /** @description In cookie mode, sets or clears the 'refresh_token' cookie (HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth), expiring with the refresh token. */
                        "Set-Cookie"?: string;
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["LoginResponse"];
                        "text/json": components["schemas"]["LoginResponse"];
                        "text/plain": components["schemas"]["LoginResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/passkeys/sign-in/options": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Starts a passkey sign-in: options for `navigator.credentials.get()` and the state to
         *     post back.
         */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PasskeySignInOptionsResponse"];
                        "text/json": components["schemas"]["PasskeySignInOptionsResponse"];
                        "text/plain": components["schemas"]["PasskeySignInOptionsResponse"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/refresh": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Exchanges a refresh token for a new pair. */
        post: {
            parameters: {
                query?: never;
                header?: {
                    /** @description Send 'cookie' to present the refresh token in the 'refresh_token' cookie instead of the body; the cookie is read only when this header is sent, which is what refuses a request forged from another site. */
                    "X-Refresh-Token-Mode"?: string;
                };
                path?: never;
                cookie?: {
                    /** @description The refresh token, read only when X-Refresh-Token-Mode is 'cookie'. */
                    refresh_token?: string;
                };
            };
            requestBody?: {
                content: {
                    "application/*+json": null | components["schemas"]["RefreshAccessTokenRequest"];
                    "application/json": null | components["schemas"]["RefreshAccessTokenRequest"];
                    "text/json": null | components["schemas"]["RefreshAccessTokenRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        /** @description In cookie mode, sets or clears the 'refresh_token' cookie (HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth), expiring with the refresh token. */
                        "Set-Cookie"?: string;
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TokenResponse"];
                        "text/json": components["schemas"]["TokenResponse"];
                        "text/plain": components["schemas"]["TokenResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        /** @description In cookie mode, sets or clears the 'refresh_token' cookie (HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth), expiring with the refresh token. */
                        "Set-Cookie"?: string;
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/register": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Requests an account for an address and mails the address what happened. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["RegisterRequest"];
                    "application/json": components["schemas"]["RegisterRequest"];
                    "text/json": components["schemas"]["RegisterRequest"];
                };
            };
            responses: {
                /** @description Accepted */
                202: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RegisterResponse"];
                        "text/json": components["schemas"]["RegisterResponse"];
                        "text/plain": components["schemas"]["RegisterResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/resend-confirmation-email": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Re-sends the confirmation email. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["ResendConfirmationEmailRequest"];
                    "application/json": components["schemas"]["ResendConfirmationEmailRequest"];
                    "text/json": components["schemas"]["ResendConfirmationEmailRequest"];
                };
            };
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/reset-password": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Sets a new password from a reset token. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["ResetPasswordRequest"];
                    "application/json": components["schemas"]["ResetPasswordRequest"];
                    "text/json": components["schemas"]["ResetPasswordRequest"];
                };
            };
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/sessions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * Lists the caller's live sessions, most recently used first, marking the one making this
         *     request.
         */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["SessionsResponse"];
                        "text/json": components["schemas"]["SessionsResponse"];
                        "text/plain": components["schemas"]["SessionsResponse"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/sessions/{sessionId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        /** Ends one of the caller's sessions by the id the session list names. */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    sessionId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/two-factor/confirm": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Confirms enrollment with a first code and turns two-factor sign-in on. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["ConfirmTwoFactorSetupRequest"];
                    "application/json": components["schemas"]["ConfirmTwoFactorSetupRequest"];
                    "text/json": components["schemas"]["ConfirmTwoFactorSetupRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ConfirmTwoFactorSetupResponse"];
                        "text/json": components["schemas"]["ConfirmTwoFactorSetupResponse"];
                        "text/plain": components["schemas"]["ConfirmTwoFactorSetupResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/two-factor/disable": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Turns two-factor sign-in back off. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["DisableTwoFactorRequest"];
                    "application/json": components["schemas"]["DisableTwoFactorRequest"];
                    "text/json": components["schemas"]["DisableTwoFactorRequest"];
                };
            };
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/auth/two-factor/setup": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Begins two-factor enrollment and returns the shared key, raw and as an `otpauth://`
         *     URI.
         */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["SetUpTwoFactorResponse"];
                        "text/json": components["schemas"]["SetUpTwoFactorResponse"];
                        "text/plain": components["schemas"]["SetUpTwoFactorResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/external-identities/{userId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        /** Deletes the account's local presence: every mapping that names it, and its assigned roles. */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    userId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Forbidden */
                403: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/files": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Lists the caller's own files, sorted, filtered and paginated. */
        get: {
            parameters: {
                query?: {
                    Cursor?: string;
                    Page?: number | string;
                    PageSize?: number | string;
                    Paging?: string;
                    Search?: string;
                    Sort?: string;
                    /** @description `pending`, `deposited`, `available` or `quarantined`; blank means all four. */
                    State?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PagedResponseOfStoredFileResponse"];
                        "text/json": components["schemas"]["PagedResponseOfStoredFileResponse"];
                        "text/plain": components["schemas"]["PagedResponseOfStoredFileResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        put?: never;
        /** Reserves a place for a file and hands back the right to deposit its bytes. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["RegisterFileRequest"];
                    "application/json": components["schemas"]["RegisterFileRequest"];
                    "text/json": components["schemas"]["RegisterFileRequest"];
                };
            };
            responses: {
                /** @description Created */
                201: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["StoredFileRegistrationResponse"];
                        "text/json": components["schemas"]["StoredFileRegistrationResponse"];
                        "text/plain": components["schemas"]["StoredFileRegistrationResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/files/{fileId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Gets one file's metadata, and the `ETag` needed to change it. */
        get: operations["GetFile"];
        put?: never;
        post?: never;
        /** Deletes a file and lets its bytes be reclaimed. */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    fileId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Failed */
                412: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Required */
                428: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        options?: never;
        /** Gets one file's metadata, and the `ETag` needed to change it. */
        head: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    fileId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["StoredFileResponse"];
                        "text/json": components["schemas"]["StoredFileResponse"];
                        "text/plain": components["schemas"]["StoredFileResponse"];
                    };
                };
                /** @description Not Modified */
                304: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        patch?: never;
        trace?: never;
    };
    "/api/v1/files/{fileId}/confirm": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Confirms that the bytes have been deposited, moving the file to `deposited`. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    fileId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["StoredFileResponse"];
                        "text/json": components["schemas"]["StoredFileResponse"];
                        "text/plain": components["schemas"]["StoredFileResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Failed */
                412: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Required */
                428: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/files/{fileId}/content": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** The file's content: a redirect to a signed URL, or the bytes themselves. */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    fileId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/octet-stream": components["schemas"]["Stream"];
                    };
                };
                /** @description Found */
                302: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/files/{fileId}/tags": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        /** Replaces the labels a file carries. */
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    fileId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["ReplaceStoredFileTagsRequest"];
                    "application/json": components["schemas"]["ReplaceStoredFileTagsRequest"];
                    "text/json": components["schemas"]["ReplaceStoredFileTagsRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["StoredFileResponse"];
                        "text/json": components["schemas"]["StoredFileResponse"];
                        "text/plain": components["schemas"]["StoredFileResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Failed */
                412: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Required */
                428: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/files/tags": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** The tags the caller has already used on their files. */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["UsedTagsResponse"];
                        "text/json": components["schemas"]["UsedTagsResponse"];
                        "text/plain": components["schemas"]["UsedTagsResponse"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/files/uploads": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Uploads a file through the API: registers it, stores its bytes and confirms the deposit. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            /** @description A label and download name; the part's file name when absent or blank. */
            requestBody: {
                content: {
                    "multipart/form-data": {
                        Category?: string;
                        File?: components["schemas"]["IFormFile"];
                        Name?: string;
                    };
                };
            };
            responses: {
                /** @description Created */
                201: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["StoredFileResponse"];
                        "text/json": components["schemas"]["StoredFileResponse"];
                        "text/plain": components["schemas"]["StoredFileResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/maintenance/idempotency-keys/expired": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        /** Deletes every idempotency key whose retention window has passed. */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PurgeResponse"];
                        "text/json": components["schemas"]["PurgeResponse"];
                        "text/plain": components["schemas"]["PurgeResponse"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Forbidden */
                403: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/maintenance/refresh-tokens/expired": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        /** Deletes every refresh-token grant whose retention window has passed. */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PurgeResponse"];
                        "text/json": components["schemas"]["PurgeResponse"];
                        "text/plain": components["schemas"]["PurgeResponse"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Forbidden */
                403: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/maintenance/security-events/expired": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        /** Deletes every security event older than the retention period, for every account. */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PurgeResponse"];
                        "text/json": components["schemas"]["PurgeResponse"];
                        "text/plain": components["schemas"]["PurgeResponse"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Forbidden */
                403: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/notes": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Lists the caller's own notes, sorted and paginated. */
        get: {
            parameters: {
                query?: {
                    Cursor?: string;
                    Page?: number | string;
                    PageSize?: number | string;
                    Paging?: string;
                    Sort?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PagedResponseOfNoteSummaryResponse"];
                        "text/json": components["schemas"]["PagedResponseOfNoteSummaryResponse"];
                        "text/plain": components["schemas"]["PagedResponseOfNoteSummaryResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        put?: never;
        /** Creates a note owned by the caller. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["CreateNoteRequest"];
                    "application/json": components["schemas"]["CreateNoteRequest"];
                    "text/json": components["schemas"]["CreateNoteRequest"];
                };
            };
            responses: {
                /** @description Created */
                201: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["NoteResponse"];
                        "text/json": components["schemas"]["NoteResponse"];
                        "text/plain": components["schemas"]["NoteResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/notes/{noteId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Gets one note, and the `ETag` needed to change it. */
        get: operations["GetNote"];
        /** Replaces a note's title and body. */
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    noteId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["UpdateNoteRequest"];
                    "application/json": components["schemas"]["UpdateNoteRequest"];
                    "text/json": components["schemas"]["UpdateNoteRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["NoteResponse"];
                        "text/json": components["schemas"]["NoteResponse"];
                        "text/plain": components["schemas"]["NoteResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Failed */
                412: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Required */
                428: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        post?: never;
        /** Deletes a note, on the same `If-Match` terms as the update. */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    noteId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Failed */
                412: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Required */
                428: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        options?: never;
        /** Gets one note, and the `ETag` needed to change it. */
        head: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    noteId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["NoteResponse"];
                        "text/json": components["schemas"]["NoteResponse"];
                        "text/plain": components["schemas"]["NoteResponse"];
                    };
                };
                /** @description Not Modified */
                304: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        patch?: never;
        trace?: never;
    };
    "/api/v1/reminders/{reminderId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        /** Reschedules a reminder to a new due date. */
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    reminderId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["RescheduleReminderRequest"];
                    "application/json": components["schemas"]["RescheduleReminderRequest"];
                    "text/json": components["schemas"]["RescheduleReminderRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ReminderResponse"];
                        "text/json": components["schemas"]["ReminderResponse"];
                        "text/plain": components["schemas"]["ReminderResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Failed */
                412: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Required */
                428: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        post?: never;
        /** Cancels a reminder before it fires. */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    reminderId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Failed */
                412: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Required */
                428: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/todo-lists": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Lists the caller's own todo lists, sorted, filtered and paginated. */
        get: {
            parameters: {
                query?: {
                    CreatedAfter?: string;
                    CreatedBefore?: string;
                    Cursor?: string;
                    Page?: number | string;
                    PageSize?: number | string;
                    Paging?: string;
                    Search?: string;
                    Sort?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PagedResponseOfTodoListSummaryResponse"];
                        "text/json": components["schemas"]["PagedResponseOfTodoListSummaryResponse"];
                        "text/plain": components["schemas"]["PagedResponseOfTodoListSummaryResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        put?: never;
        /** Creates a todo list owned by the caller. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["CreateTodoListRequest"];
                    "application/json": components["schemas"]["CreateTodoListRequest"];
                    "text/json": components["schemas"]["CreateTodoListRequest"];
                };
            };
            responses: {
                /** @description Created */
                201: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TodoListResponse"];
                        "text/json": components["schemas"]["TodoListResponse"];
                        "text/plain": components["schemas"]["TodoListResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/todo-lists/{todoListId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Gets one todo list with its items, and the `ETag` needed to change it. */
        get: operations["GetTodoList"];
        /** Renames a todo list. */
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    todoListId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["RenameTodoListRequest"];
                    "application/json": components["schemas"]["RenameTodoListRequest"];
                    "text/json": components["schemas"]["RenameTodoListRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TodoListResponse"];
                        "text/json": components["schemas"]["TodoListResponse"];
                        "text/plain": components["schemas"]["TodoListResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Failed */
                412: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Required */
                428: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        post?: never;
        /** Deletes a todo list and its items. */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    todoListId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description No Content */
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Failed */
                412: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Required */
                428: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        options?: never;
        /** Gets one todo list with its items, and the `ETag` needed to change it. */
        head: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    todoListId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TodoListResponse"];
                        "text/json": components["schemas"]["TodoListResponse"];
                        "text/plain": components["schemas"]["TodoListResponse"];
                    };
                };
                /** @description Not Modified */
                304: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        patch?: never;
        trace?: never;
    };
    "/api/v1/todo-lists/{todoListId}/items": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Gets every item of a todo list. */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    todoListId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TodoItemsResponse"];
                        "text/json": components["schemas"]["TodoItemsResponse"];
                        "text/plain": components["schemas"]["TodoItemsResponse"];
                    };
                };
                /** @description Not Modified */
                304: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        put?: never;
        /** Adds an item to a todo list. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    todoListId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["AddTodoItemRequest"];
                    "application/json": components["schemas"]["AddTodoItemRequest"];
                    "text/json": components["schemas"]["AddTodoItemRequest"];
                };
            };
            responses: {
                /** @description Created */
                201: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TodoItemResponse"];
                        "text/json": components["schemas"]["TodoItemResponse"];
                        "text/plain": components["schemas"]["TodoItemResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Failed */
                412: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Required */
                428: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        /** Gets every item of a todo list. */
        head: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    todoListId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TodoItemsResponse"];
                        "text/json": components["schemas"]["TodoItemsResponse"];
                        "text/plain": components["schemas"]["TodoItemsResponse"];
                    };
                };
                /** @description Not Modified */
                304: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        patch?: never;
        trace?: never;
    };
    "/api/v1/todo-lists/{todoListId}/items/{todoItemId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Gets one item of a todo list. */
        get: operations["GetItemById"];
        /** Replaces an item's title and description. */
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    todoItemId: string;
                    todoListId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["UpdateTodoItemRequest"];
                    "application/json": components["schemas"]["UpdateTodoItemRequest"];
                    "text/json": components["schemas"]["UpdateTodoItemRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TodoItemResponse"];
                        "text/json": components["schemas"]["TodoItemResponse"];
                        "text/plain": components["schemas"]["TodoItemResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Failed */
                412: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Required */
                428: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        post?: never;
        /** Removes an item from a todo list. */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    todoItemId: string;
                    todoListId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TodoListResponse"];
                        "text/json": components["schemas"]["TodoListResponse"];
                        "text/plain": components["schemas"]["TodoListResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Failed */
                412: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Required */
                428: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        options?: never;
        /** Gets one item of a todo list. */
        head: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    todoItemId: string;
                    todoListId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TodoItemResponse"];
                        "text/json": components["schemas"]["TodoItemResponse"];
                        "text/plain": components["schemas"]["TodoItemResponse"];
                    };
                };
                /** @description Not Modified */
                304: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        patch?: never;
        trace?: never;
    };
    "/api/v1/todo-lists/{todoListId}/items/{todoItemId}/complete": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Marks an item as completed. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    todoItemId: string;
                    todoListId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TodoItemResponse"];
                        "text/json": components["schemas"]["TodoItemResponse"];
                        "text/plain": components["schemas"]["TodoItemResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Failed */
                412: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Required */
                428: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/todo-lists/{todoListId}/items/{todoItemId}/reminders": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Gets every reminder scheduled for one item. */
        get: operations["GetForItem"];
        put?: never;
        /** Schedules a reminder for one item. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    todoItemId: string;
                    todoListId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["ScheduleReminderRequest"];
                    "application/json": components["schemas"]["ScheduleReminderRequest"];
                    "text/json": components["schemas"]["ScheduleReminderRequest"];
                };
            };
            responses: {
                /** @description Created */
                201: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ReminderResponse"];
                        "text/json": components["schemas"]["ReminderResponse"];
                        "text/plain": components["schemas"]["ReminderResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/todo-lists/{todoListId}/items/{todoItemId}/reopen": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Marks a completed item as not completed again. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    todoItemId: string;
                    todoListId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TodoItemResponse"];
                        "text/json": components["schemas"]["TodoItemResponse"];
                        "text/plain": components["schemas"]["TodoItemResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Failed */
                412: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Required */
                428: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/todo-lists/{todoListId}/items/{todoItemId}/tags": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        /** Replaces an item's whole tag set. */
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    todoItemId: string;
                    todoListId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["ReplaceTodoItemTagsRequest"];
                    "application/json": components["schemas"]["ReplaceTodoItemTagsRequest"];
                    "text/json": components["schemas"]["ReplaceTodoItemTagsRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TodoItemResponse"];
                        "text/json": components["schemas"]["TodoItemResponse"];
                        "text/plain": components["schemas"]["TodoItemResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Failed */
                412: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Required */
                428: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        /** Adds one tag to an item. */
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    todoItemId: string;
                    todoListId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/*+json": components["schemas"]["AddTodoItemTagRequest"];
                    "application/json": components["schemas"]["AddTodoItemTagRequest"];
                    "text/json": components["schemas"]["AddTodoItemTagRequest"];
                };
            };
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TodoItemResponse"];
                        "text/json": components["schemas"]["TodoItemResponse"];
                        "text/plain": components["schemas"]["TodoItemResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Failed */
                412: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Required */
                428: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/todo-lists/{todoListId}/items/{todoItemId}/tags/{tag}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        /** Removes one tag from an item. */
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    tag: string;
                    todoItemId: string;
                    todoListId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TodoItemResponse"];
                        "text/json": components["schemas"]["TodoItemResponse"];
                        "text/plain": components["schemas"]["TodoItemResponse"];
                    };
                };
                /** @description Bad Request */
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ValidationProblemDetails"];
                        "text/json": components["schemas"]["ValidationProblemDetails"];
                        "text/plain": components["schemas"]["ValidationProblemDetails"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Not Found */
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Conflict */
                409: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Failed */
                412: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Precondition Required */
                428: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/v1/todo-lists/tags": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** The tags the caller has already used on their items. */
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                /** @description OK */
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["UsedTagsResponse"];
                        "text/json": components["schemas"]["UsedTagsResponse"];
                        "text/plain": components["schemas"]["UsedTagsResponse"];
                    };
                };
                /** @description Unauthorized */
                401: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Payload Too Large */
                413: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Unsupported Media Type */
                415: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Too Many Requests */
                429: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
                /** @description Internal Server Error */
                500: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProblemDetails"];
                        "text/json": components["schemas"]["ProblemDetails"];
                        "text/plain": components["schemas"]["ProblemDetails"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
}
export type webhooks = Record<string, never>;
export interface components {
    schemas: {
        /** @description Carries no list id: that travels in the route. */
        AddTodoItemRequest: {
            description: null | string;
            /** @description Normalised and de-duplicated by the domain. */
            tags: null | string[];
            /** @description Must be unique within its list. */
            title: string;
        };
        /** @description Carries neither the list id nor the item id: both travel in the route. */
        AddTodoItemTagRequest: {
            /** @description Normalised to lower case by the domain. */
            tag: string;
        };
        /** @description Carries no identity: the account is the authenticated caller's own. */
        ChangePasswordRequest: {
            currentPassword: string;
            newPassword: string;
        };
        /**
         * @description The created credential, the state its options came with, an optional label and the current
         *     password.
         */
        CompletePasskeyRegistrationRequest: {
            /** @description The JSON-serialised result of `navigator.credentials.create()`. */
            credential: string;
            /** @description The account's password. */
            currentPassword: null | string;
            /** @description An optional label for the credential, shown in the passkey list. */
            name: null | string;
            /** @description The opaque state string the registration options returned. */
            registrationState: string;
        };
        /** @description The new address and the token mailed to it, sent in the body as ConfirmEmailRequest is. */
        ConfirmEmailChangeRequest: {
            newEmail: string;
            token: string;
        };
        /** @description The address and its single-use token, sent in the body so the token stays out of logs and URLs. */
        ConfirmEmailRequest: {
            email: string;
            token: string;
        };
        /** @description Carries no identity: the pending secret is the authenticated caller's own. */
        ConfirmTwoFactorSetupRequest: {
            code: string;
            currentPassword: string;
        };
        ConfirmTwoFactorSetupResponse: {
            /** @description Shown once and never retrievable again. */
            recoveryCodes: string[];
        };
        /** @description The owner is the authenticated caller and is not a field here. */
        CreateNoteRequest: {
            /** @description Required but may be empty, at most 10000 characters. */
            body: string;
            /** @description Required, at most 200 characters once trimmed. */
            title: string;
        };
        /** @description The name of a new todo list. */
        CreateTodoListRequest: {
            name: string;
        };
        /** @description The authenticated caller's own profile. */
        CurrentUserResponse: {
            /** Format: date-time */
            createdAt: string;
            email: string;
            emailConfirmed: boolean;
            roles: string[];
            twoFactorEnabled: boolean;
            /** Format: uuid */
            userId: string;
        };
        /**
         * @description The proof the account demands: its current password, a two-factor code, or neither when
         *     provider-linked.
         */
        DeleteMyAccountRequest: {
            currentPassword: null | string;
            twoFactorCode: null | string;
        };
        /** @description Carries no identity: the account is the authenticated caller's own. */
        DisableTwoFactorRequest: {
            currentPassword: string;
        };
        /** @description A provider sign-in's answer, with the branches, `status` tag and names of LoginResponse. */
        ExternalLoginResponse: components["schemas"]["ExternalLoginResponseAuthenticated"] | components["schemas"]["ExternalLoginResponseTwoFactorRequired"];
        ExternalLoginResponseAuthenticated: {
            /** @description Whether this call is what created the account. */
            accountCreated: boolean;
            /** @enum {string} */
            status?: "authenticated";
            /** @description The token pair, nested as in Authenticated. */
            tokens: components["schemas"]["TokenResponse"];
        };
        /** @description The account has a second factor armed, and the provider does not stand in for it. */
        ExternalLoginResponseTwoFactorRequired: {
            /** @description Identifies the pending sign-in. */
            challengeToken: string;
            /** @enum {string} */
            status?: "twoFactorRequired";
        };
        /** Format: binary */
        IFormFile: string;
        LoginRequest: {
            email: string;
            password: string;
        };
        /**
         * @description A token pair, or a challenge to complete with `POST /auth/login/two-factor` when two-factor
         *     sign-in is armed.
         */
        LoginResponse: components["schemas"]["LoginResponseAuthenticated"] | components["schemas"]["LoginResponseTwoFactorRequired"];
        /** @description A completed sign-in, its token pair under `tokens`. */
        LoginResponseAuthenticated: {
            /** @enum {string} */
            status?: "authenticated";
            tokens: components["schemas"]["TokenResponse"];
        };
        LoginResponseTwoFactorRequired: {
            /** @description Identifies the pending sign-in for `POST /auth/login/two-factor`. */
            challengeToken: string;
            /** @enum {string} */
            status?: "twoFactorRequired";
        };
        LogoutRequest: {
            refreshToken: string;
        };
        /** @description The wire shape of one note. */
        NoteResponse: {
            body: string;
            /** Format: date-time */
            createdAt: string;
            /** Format: uuid */
            id: string;
            /** Format: date-time */
            lastModifiedAt: null | string;
            title: string;
        };
        /** @description The wire shape of one note inside a page, without its body. */
        NoteSummaryResponse: {
            /** Format: date-time */
            createdAt: string;
            /** Format: uuid */
            id: string;
            /** Format: date-time */
            lastModifiedAt: null | string;
            title: string;
        };
        /** @description One page of results, and what is needed to ask for the next. */
        PagedResponseOfNoteSummaryResponse: {
            hasNextPage: boolean;
            items: components["schemas"]["NoteSummaryResponse"][];
            nextCursor: null | string;
            /** Format: int32 */
            page: null | number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int32 */
            totalCount: null | number | string;
            /** Format: int32 */
            totalPages: null | number | string;
        };
        /** @description One page of results, and what is needed to ask for the next. */
        PagedResponseOfSecurityEventResponse: {
            hasNextPage: boolean;
            items: components["schemas"]["SecurityEventResponse"][];
            nextCursor: null | string;
            /** Format: int32 */
            page: null | number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int32 */
            totalCount: null | number | string;
            /** Format: int32 */
            totalPages: null | number | string;
        };
        /** @description One page of results, and what is needed to ask for the next. */
        PagedResponseOfStoredFileResponse: {
            hasNextPage: boolean;
            items: components["schemas"]["StoredFileResponse"][];
            nextCursor: null | string;
            /** Format: int32 */
            page: null | number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int32 */
            totalCount: null | number | string;
            /** Format: int32 */
            totalPages: null | number | string;
        };
        /** @description One page of results, and what is needed to ask for the next. */
        PagedResponseOfTodoListSummaryResponse: {
            hasNextPage: boolean;
            items: components["schemas"]["TodoListSummaryResponse"][];
            nextCursor: null | string;
            /** Format: int32 */
            page: null | number | string;
            /** Format: int32 */
            pageSize: number | string;
            /** Format: int32 */
            totalCount: null | number | string;
            /** Format: int32 */
            totalPages: null | number | string;
        };
        /**
         * @description What a client feeds to `navigator.credentials.create()`, and the state to post back with
         *     the credential.
         */
        PasskeyRegistrationOptionsResponse: {
            /** @description The WebAuthn creation options, as a JSON string to pass as `publicKey`. */
            creationOptions: string;
            /** @description Opaque to the client; posted back when the registration completes. */
            registrationState: string;
        };
        /** @description One of the account's registered passkeys, as the passkey list shows it. */
        PasskeyResponse: {
            /**
             * Format: date-time
             * @description When it was registered.
             */
            createdAt: string;
            /** @description The credential's WebAuthn id, base64url-encoded: what the removal route takes. */
            id: string;
            /** @description The label its holder gave it when registering, or `null` when none. */
            name: null | string;
        };
        /**
         * @description What a client feeds to `navigator.credentials.get()`, and the state to post back with the
         *     assertion.
         */
        PasskeySignInOptionsResponse: {
            /** @description Opaque to the client; posted back when the sign-in completes. */
            authenticationState: string;
            /** @description The WebAuthn request options, as a JSON string to pass as `publicKey`. */
            signInOptions: string;
        };
        /** @description The assertion a client obtained from the sign-in options, and the state that came with them. */
        PasskeySignInRequest: {
            /** @description The opaque state string the sign-in options returned. */
            authenticationState: string;
            /** @description The JSON-serialised result of `navigator.credentials.get()`. */
            credential: string;
        };
        /** @description The passkeys registered on the caller's account, the newest first. */
        PasskeysResponse: {
            passkeys: components["schemas"]["PasskeyResponse"][];
        };
        ProblemDetails: {
            detail?: null | string;
            instance?: null | string;
            /** Format: int32 */
            status?: null | number | string;
            title?: null | string;
            type?: null | string;
        };
        /** @description The outcome of a purge, shared by every purge endpoint. */
        PurgeResponse: {
            /**
             * Format: int32
             * @description Rows removed by this call.
             */
            deleted: number | string;
        };
        RefreshAccessTokenRequest: {
            refreshToken: string;
        };
        /** @description The metadata of a file about to be deposited directly on the store. */
        RegisterFileRequest: {
            /** @description One of the categories configured in `Files:Categories`. */
            category: string;
            /** @description SHA-256 of the content, 64 hexadecimal characters. */
            checksum: string;
            /** @description Checked against the leading bytes when the category verifies signatures. */
            declaredMediaType: string;
            /** @description A label to show, and the name a download is offered under. */
            name: string;
            /**
             * Format: int64
             * @description The exact length the store will accept for the deposit.
             */
            sizeInBytes: number | string;
        };
        /** @description The address and password an account is requested with. */
        RegisterRequest: {
            email: string;
            password: string;
        };
        /** @description The one answer a well-formed registration gets, whatever the state of the address. */
        RegisterResponse: {
            status: string;
        };
        /** @description One reminder, also the body of every write that addresses it. */
        ReminderResponse: {
            /** Format: date-time */
            dueAt: string;
            /** Format: uuid */
            id: string;
            /**
             * Format: date-time
             * @description When the reminder actually fired, or `null` before it does.
             */
            notifiedAt: null | string;
            /** @description `pending`, `fired` or `cancelled`, never going back to an earlier value. */
            status: string;
            /** Format: uuid */
            todoItemId: string;
            /**
             * Format: uuid
             * @description The list the reminder's item belongs to.
             */
            todoListId: string;
        };
        /** @description Every reminder scheduled for one item. */
        RemindersResponse: {
            /** @description Ordered by due date. */
            reminders: components["schemas"]["ReminderResponse"][];
        };
        /** @description Carries no list id: that travels in the route. */
        RenameTodoListRequest: {
            name: string;
        };
        /** @description The labels a file should carry afterwards. */
        ReplaceStoredFileTagsRequest: {
            /** @description The complete set: anything absent is removed, and an empty list clears the tags. */
            tags: string[];
        };
        /** @description Carries neither the list id nor the item id: both travel in the route. */
        ReplaceTodoItemTagsRequest: {
            /** @description The complete set the item should end up with; an empty list clears them all. */
            tags: string[];
        };
        /** @description The new address and the current password of the authenticated caller's own account. */
        RequestEmailChangeRequest: {
            currentPassword: string;
            newEmail: string;
        };
        RequestPasswordResetRequest: {
            email: string;
        };
        /** @description Carries no reminder id: that travels in the route. */
        RescheduleReminderRequest: {
            /**
             * Format: date-time
             * @description The new due date.
             */
            dueAt: string;
        };
        ResendConfirmationEmailRequest: {
            email: string;
        };
        /** @description The reset token mailed to the address and the new password, sent in the body as ConfirmEmailRequest is. */
        ResetPasswordRequest: {
            email: string;
            newPassword: string;
            token: string;
        };
        /** @description Carries no list or item id: both travel in the route. */
        ScheduleReminderRequest: {
            /**
             * Format: date-time
             * @description Must be in the future at the moment the use case runs.
             */
            dueAt: string;
        };
        /** @description One recorded security event as it reads back. */
        SecurityEventResponse: {
            detail: null | string;
            /** Format: uuid */
            id: string;
            ipAddress: null | string;
            kind: string;
            /** Format: date-time */
            occurredAt: string;
            userAgent: null | string;
        };
        /** @description One of the caller's live sessions, as the session list shows it. */
        SessionResponse: {
            /**
             * Format: date-time
             * @description When the sign-in that started it happened.
             */
            createdAt: string;
            /** @description Whether the request was made from this session. */
            current: boolean;
            /**
             * Format: uuid
             * @description The session's id: what the revoke endpoint takes to end it.
             */
            id: string;
            /**
             * Format: date-time
             * @description When its refresh token was last rotated, or DateTimeOffset SessionResponse.CreatedAt when it never was.
             */
            lastUsedAt: string;
            /** @description The client's `User-Agent` at its last use, shortened, or `null` when it said nothing. */
            userAgent: null | string;
        };
        /** @description The caller's live sessions, most recently used first. */
        SessionsResponse: {
            sessions: components["schemas"]["SessionResponse"][];
        };
        SetUpTwoFactorResponse: {
            /** @description The same secret as an `otpauth://` URI, for a QR code. */
            authenticatorUri: string;
            /** @description The raw secret, for typing into an authenticator app by hand. */
            sharedKey: string;
        };
        /** @description What the client sends after running the provider's authorisation-code-with-PKCE flow itself. */
        SignInWithExternalProviderRequest: {
            /** @description The OpenID Connect `id_token` the provider issued to the client. */
            idToken: string;
            /** @description The provider's name as the operator wrote it in configuration. */
            provider: string;
        };
        /** @description The registered file's id and the right to deposit its bytes. */
        StoredFileRegistrationResponse: {
            /** Format: uuid */
            id: string;
            upload: components["schemas"]["UploadGrantResponse"];
        };
        /** @description The wire shape of one file, without its content (see `GET .../content`). */
        StoredFileResponse: {
            /**
             * Format: date-time
             * @description `null` until the file is released by inspection.
             */
            availableAt: null | string;
            /** @description The category the file was registered under, as configured in `Files:Categories`. */
            category: string;
            /** @description SHA-256 of the content, 64 lower-case hexadecimal characters. */
            checksum: string;
            /** @description What the client said the file is. */
            declaredMediaType: string;
            /** Format: uuid */
            id: string;
            name: string;
            /** Format: date-time */
            registeredAt: string;
            /**
             * Format: int64
             * @description Declared while `pending`, agreed with the store once deposited.
             */
            sizeInBytes: number | string;
            /** @description `pending`, `deposited`, `available` or `quarantined`. */
            status: string;
            tags: string[];
        };
        /** Format: binary */
        Stream: string;
        /** @description One item, also the body of every write that addresses it. */
        TodoItemResponse: {
            /** Format: date-time */
            completedAt: null | string;
            description: null | string;
            /** Format: uuid */
            id: string;
            isCompleted: boolean;
            /** @description Normalised to lower case. */
            tags: string[];
            /** @description Unique within its list. */
            title: string;
        };
        /** @description The items of one todo list. */
        TodoItemsResponse: {
            /** @description Ordered by title. */
            items: components["schemas"]["TodoItemResponse"][];
        };
        /** @description One todo list with all its items. */
        TodoListResponse: {
            /** Format: date-time */
            createdAt: string;
            /** Format: uuid */
            id: string;
            /** @description Ordered by title. */
            items: components["schemas"]["TodoItemResponse"][];
            /** Format: date-time */
            lastModifiedAt: null | string;
            name: string;
        };
        /** @description The wire shape of one list inside a page. */
        TodoListSummaryResponse: {
            /** Format: int32 */
            completedItemCount: number | string;
            /** Format: date-time */
            createdAt: string;
            /** Format: uuid */
            id: string;
            /** Format: int32 */
            itemCount: number | string;
            name: string;
        };
        /** @description One token pair, in the same shape from sign-in and from refresh. */
        TokenResponse: {
            accessToken: string;
            /** Format: date-time */
            accessTokenExpiresAt: string;
            /** @description The raw secret, returned once and never persisted in this form. Absent in cookie mode, where the 'refresh_token' cookie carries it. */
            refreshToken?: string;
            /** Format: date-time */
            refreshTokenExpiresAt: string;
        };
        /** @description Carries no note id: that travels in the route. */
        UpdateNoteRequest: {
            /** @description Required but may be empty, at most 10000 characters. */
            body: string;
            /** @description Required, at most 200 characters once trimmed. */
            title: string;
        };
        /** @description The complete title/description representation of one item. */
        UpdateTodoItemRequest: {
            /** @description Absent clears it: this is the whole representation, not a patch. */
            description: null | string;
            /** @description Must stay unique within its list, excluding the item itself. */
            title: string;
        };
        /** @description The right to deposit one file's bytes directly on the object store. */
        UploadGrantResponse: {
            /**
             * Format: date-time
             * @description After this instant the URL is worthless; register again.
             */
            expiresAt: string;
            /** @description The HTTP method the signature covers. */
            method: string;
            /** @description Headers the signature covers; the deposit must send them verbatim. */
            requiredHeaders: {
                [key: string]: string;
            };
            /** @description Signed, single-purpose and short-lived; do not log, persist or share it. */
            url: string;
        };
        /** @description The tags the caller has already used on their items. */
        UsedTagsResponse: {
            /** @description Every tag in use, each once, ordered so two calls agree. */
            tags: string[];
        };
        ValidationProblemDetails: {
            detail?: null | string;
            errors?: {
                [key: string]: string[];
            };
            instance?: null | string;
            /** Format: int32 */
            status?: null | number | string;
            title?: null | string;
            type?: null | string;
        };
        VerifyTwoFactorRequest: {
            challengeToken: string;
            /** @description Either the authenticator app's current six digits or one of the recovery codes. */
            code: string;
        };
    };
    responses: never;
    parameters: never;
    requestBodies: never;
    headers: never;
    pathItems: never;
}
export type $defs = Record<string, never>;
export interface operations {
    GetFile: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                fileId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["StoredFileResponse"];
                    "text/json": components["schemas"]["StoredFileResponse"];
                    "text/plain": components["schemas"]["StoredFileResponse"];
                };
            };
            /** @description Not Modified */
            304: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ValidationProblemDetails"];
                    "text/json": components["schemas"]["ValidationProblemDetails"];
                    "text/plain": components["schemas"]["ValidationProblemDetails"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Payload Too Large */
            413: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Unsupported Media Type */
            415: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Internal Server Error */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
        };
    };
    GetNote: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                noteId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["NoteResponse"];
                    "text/json": components["schemas"]["NoteResponse"];
                    "text/plain": components["schemas"]["NoteResponse"];
                };
            };
            /** @description Not Modified */
            304: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Payload Too Large */
            413: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Unsupported Media Type */
            415: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Internal Server Error */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
        };
    };
    GetTodoList: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                todoListId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["TodoListResponse"];
                    "text/json": components["schemas"]["TodoListResponse"];
                    "text/plain": components["schemas"]["TodoListResponse"];
                };
            };
            /** @description Not Modified */
            304: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Payload Too Large */
            413: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Unsupported Media Type */
            415: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Internal Server Error */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
        };
    };
    GetItemById: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                todoItemId: string;
                todoListId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["TodoItemResponse"];
                    "text/json": components["schemas"]["TodoItemResponse"];
                    "text/plain": components["schemas"]["TodoItemResponse"];
                };
            };
            /** @description Not Modified */
            304: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Not Found */
            404: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Payload Too Large */
            413: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Unsupported Media Type */
            415: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Internal Server Error */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
        };
    };
    GetForItem: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                todoItemId: string;
                todoListId: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description OK */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["RemindersResponse"];
                    "text/json": components["schemas"]["RemindersResponse"];
                    "text/plain": components["schemas"]["RemindersResponse"];
                };
            };
            /** @description Bad Request */
            400: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ValidationProblemDetails"];
                    "text/json": components["schemas"]["ValidationProblemDetails"];
                    "text/plain": components["schemas"]["ValidationProblemDetails"];
                };
            };
            /** @description Unauthorized */
            401: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Payload Too Large */
            413: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Unsupported Media Type */
            415: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Too Many Requests */
            429: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
            /** @description Internal Server Error */
            500: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProblemDetails"];
                    "text/json": components["schemas"]["ProblemDetails"];
                    "text/plain": components["schemas"]["ProblemDetails"];
                };
            };
        };
    };
}
