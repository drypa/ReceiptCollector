import {Component, OnDestroy, OnInit} from '@angular/core';
import {ActivatedRoute} from "@angular/router";
import {ReceiptService} from "../receipt.service";
import {Subject} from "rxjs";
import {first, flatMap, map, takeUntil, tap} from "rxjs/operators";
import {Receipt} from "../receipt";

@Component({
  selector: 'app-receipt-details',
  templateUrl: './receipt-details.component.html',
  styleUrls: ['./receipt-details.component.scss']
})
export class ReceiptDetailsComponent implements OnInit, OnDestroy {
  receipt: Receipt;
  private destroy$ = new Subject<boolean>();

  constructor(private route: ActivatedRoute, private receiptService: ReceiptService) {
  }

  ngOnInit() {
    this.route.paramMap.pipe(
      first(),
      map(p => p.get('id')),
      flatMap(id => this.receiptService.get(id)),
      tap(receipt => this.receipt = receipt),
      takeUntil(this.destroy$)
    ).subscribe();
  }

  ngOnDestroy(): void {
    this.destroy$.next(true);
    this.destroy$.complete();
  }

}
