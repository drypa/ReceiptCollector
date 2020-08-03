import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Waste } from './waste';

@Injectable({
  providedIn: 'root'
})
export class WasteService {

  constructor(private http: HttpClient) {
  }

  getAll(filter: Filter): Observable<Waste[]> {
    const url = `/api/waste?from=${filter.from}&to=${filter.to}`
    return this.http.get<Waste[]>(url);
  }
}


export class Filter {
  from: number;
  to: number;
}
