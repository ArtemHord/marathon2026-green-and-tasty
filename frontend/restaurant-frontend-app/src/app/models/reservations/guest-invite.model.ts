export interface GuestInvite {
  GroupReservationId: string;
  LocationId: string;
  RestaurantName: string;
  LocationAddress: string;
  StartTime: string;
  EndTime: string;
  MaxGuestCount: number;
  TableNumber: number;
  InviterName: string;
  /** Статус групи; бекенд повертає його разом з CanJoin (200 і для повної / Submitted / Closed групи). */
  Status?: 'Open' | 'Submitted' | 'Closed';
  /** false — приєднатися вже не можна (група повна, відправлена або закрита). */
  CanJoin?: boolean;
}

export interface GroupInviteToken {
  token: string;
}
