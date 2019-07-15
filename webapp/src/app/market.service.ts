import {Injectable} from '@angular/core';
import {Observable} from "rxjs";
import {HttpClient} from "@angular/common/http";
import {Market} from "./market";

@Injectable({
  providedIn: 'root'
})
export class MarketService {

  constructor(private httpClient: HttpClient) {
  }

  addMarket(name: string, inns: Array<string>, type: string): Observable<void> {
    return this.httpClient.post<void>('api/market', <Market>{name: name, inns: inns, type: type});
  }
}
