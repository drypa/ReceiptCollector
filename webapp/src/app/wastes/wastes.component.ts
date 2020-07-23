import { Component, OnDestroy, OnInit } from '@angular/core';
import { Subject } from "rxjs";
import { WasteService } from "../waste.service";
import { Waste } from "../waste";
import { first, takeUntil, tap } from "rxjs/operators";

@Component({
  selector: 'app-wastes',
  templateUrl: './wastes.component.html',
  styleUrls: ['./wastes.component.scss']
})
export class WastesComponent implements OnInit, OnDestroy {
  wastes: Waste[];
  private destroy$ = new Subject<boolean>();

  constructor(private wasteService: WasteService) {
  }

  ngOnInit(): void {
    this.loadData();
  }

  ngOnDestroy(): void {
    this.destroy$.next(true);
    this.destroy$.complete();
  }

  private loadData() {
    this.wasteService.getAll()
      .pipe(
        first(),
        tap(wastes => this.wastes = wastes),
        takeUntil(this.destroy$)
      ).subscribe();
  }

}
