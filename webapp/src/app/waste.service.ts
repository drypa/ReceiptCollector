import { Injectable } from '@angular/core';
import { HttpClient } from "@angular/common/http";
import { Observable } from "rxjs";
import { Waste } from "./waste";

@Injectable({
  providedIn: 'root'
})
export class WasteService {

  constructor(private http: HttpClient) {
  }

  getAll(): Observable<Waste[]> {
    return this.http.get<Waste[]>('/api/waste');
  }
}
