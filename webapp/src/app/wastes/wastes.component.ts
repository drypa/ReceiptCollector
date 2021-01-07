import { Component, OnDestroy, OnInit } from '@angular/core';
import { Subject } from 'rxjs';
import { Filter, WasteService } from '../waste.service';
import { Waste } from '../waste';
import { first, takeUntil, tap } from 'rxjs/operators';
import { MatDialog } from '@angular/material/dialog';
import { WasteDetailsComponent } from '../waste-details/waste-details.component';

@Component({
  selector: 'app-wastes',
  templateUrl: './wastes.component.html',
  styleUrls: ['./wastes.component.scss']
})
export class WastesComponent implements OnInit, OnDestroy {
  wastes: Waste[];
  private destroy$ = new Subject<boolean>();

  constructor(private wasteService: WasteService,
              private dialog: MatDialog) {
  }

  ngOnInit(): void {
    this.loadData();
  }

  ngOnDestroy(): void {
    this.destroy$.next(true);
    this.destroy$.complete();
  }

  onEdit(id: string) {
    this.wasteService.get(id).pipe(
      first(),
      tap(x => {
        this.dialog.open(WasteDetailsComponent, {
          width: '700px',
          data: x
        });
      }),
      takeUntil(this.destroy$)
    ).subscribe();

  }

  private loadData() {
    this.wasteService.getAll(<Filter>{
      from: Date.UTC(2016, 1, 1),
      to: Date.UTC(2021, 1, 1)
    })
      .pipe(
        first(),
        tap(wastes => this.wastes = wastes),
        takeUntil(this.destroy$)
      ).subscribe();
  }
}
