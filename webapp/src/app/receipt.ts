import {Purchase} from "./purchase";

export class Receipt {
  id: string;
  dateTime: string;
  totalSum: number;
  retailPlaceAddress: string;
  userInn: string;
  items: Purchase[];
  cashTotalSum: number;
  ecashTotalSum: number;
  user: string;
  operator: string;
  nds18: number;
  nds10: number;
  queryString: string;
  deleted: boolean;
  kktsRequestStatus: RequestStatus;
}

export enum RequestStatus {
  undefined = "undefined",
  error = "error",
  success = "success",
  notFound = "not_found",
}
