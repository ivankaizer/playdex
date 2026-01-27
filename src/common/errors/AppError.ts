export class AppError extends Error {
  statusCode: number;
  code?: string;
  isOperational: boolean;

  constructor(
    message: string,
    statusCode = 500,
    options?: { code?: string; isOperational?: boolean; cause?: unknown }
  ) {
    super(message);
    this.name = "AppError";
    this.statusCode = statusCode;
    this.code = options?.code;
    this.isOperational = options?.isOperational ?? statusCode < 500;
    if (options?.cause) {
      this.cause = options.cause;
    }
  }
}

export const isAppError = (error: unknown): error is AppError =>
  error instanceof AppError;

