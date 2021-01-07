import { Component, Input, OnInit } from '@angular/core';
import { Purchase } from '../purchase';

@Component({
  selector: 'app-receipt-items',
  templateUrl: './receipt-items.component.html',
  styleUrls: ['./receipt-items.component.scss']
})
export class ReceiptItemsComponent implements OnInit {

  @Input() purchases: Purchase[];

  constructor() {
  }

  ngOnInit() {
  }

}
