import { Place } from './place';

export class Waste {
  date: Date;
  receipt_id: string;
  place: Place;
  sum: number;
  description: string;
  owner_id: string;
  category: string;
  categoryName: string;
}
