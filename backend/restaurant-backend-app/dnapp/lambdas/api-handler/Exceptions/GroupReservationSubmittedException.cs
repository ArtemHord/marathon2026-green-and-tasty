namespace APIHandler.Exceptions;

public class GroupReservationSubmittedException(string message = "Group reservation was submitted.") : ValidationException(message);
