namespace APIHandler.Exceptions;

public class OrderNotFoundException(string message) : ValidationException(message);
